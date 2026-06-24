import 'dart:async';
import 'dart:convert';
import 'dart:io';
import 'package:flutter/foundation.dart';
import 'package:uuid/uuid.dart';
import 'package:web_socket_channel/web_socket_channel.dart';

import 'package:flutter_webrtc/flutter_webrtc.dart';

import '../utils/notifications.dart';
import 'input_channel.dart';
import 'pc_websocket.dart';
import 'screen_stream_modes.dart';
import 'session_crypto.dart';

const int kWsPortDefault = 47821;
const int kDefaultWssPort = 47824;
const int kDiscoveryPort = 47822;
const String kDiscoverProbe = 'PCONNECT_DISCOVER_V1';
const String kClientVersion = '0.2.0+1';
const int kClientProto = 2;

class DiscoveredPc {
  final String name;
  final InternetAddress address;
  final int wsPort;
  final int? wssPort;
  DiscoveredPc({required this.name, required this.address, required this.wsPort, this.wssPort});
}

class ConnectionStatus {
  final bool connected;
  final bool needsPairing;
  final String? pcName;
  final String? error;
  final String? role;
  /// Negotiated screen preview backend (e.g. `jpeg-v1`). Null when capture unavailable.
  final String? screenStream;
  /// Backends the PC advertised in `helloAck`.
  final List<String> screenStreamModes;

  const ConnectionStatus({
    required this.connected,
    required this.needsPairing,
    this.pcName,
    this.error,
    this.role,
    this.screenStream,
    this.screenStreamModes = const [],
  });

  static const disconnected = ConnectionStatus(connected: false, needsPairing: false);

  String get effectiveScreenStream => screenStream ?? ScreenStreamModes.jpegV1;

  bool get screenPreviewAvailable =>
      screenStream != null ||
      screenStreamModes.contains(ScreenStreamModes.jpegV1) ||
      screenStreamModes.contains(ScreenStreamModes.jpegBinV1) ||
      screenStreamModes.contains(ScreenStreamModes.webRtcV1);
}

class FileTransferProgress {
  final String filename;
  final int totalBytes;
  int transferredBytes;
  final DateTime startTime;
  final bool isDownload;

  FileTransferProgress({
    required this.filename,
    required this.totalBytes,
    required this.isDownload,
  })  : transferredBytes = 0,
        startTime = DateTime.now();

  double get progress => totalBytes > 0 ? transferredBytes / totalBytes : 0;
  int get elapsedSeconds => DateTime.now().difference(startTime).inSeconds;
  int get bytesPerSecond => elapsedSeconds > 0 ? transferredBytes ~/ elapsedSeconds : 0;
  int get etaSeconds => bytesPerSecond > 0 ? (totalBytes - transferredBytes) ~/ bytesPerSecond : 0;
  String get progressStr => '${(progress * 100).toStringAsFixed(1)}%';
}

class RemoteFile {
  final String path;
  final String name;
  final int modified;
  final int size;
  RemoteFile({required this.path, required this.name, required this.modified, required this.size});

  String get sizeStr {
    if (size < 1024) return '$size B';
    if (size < 1024 * 1024) return '${(size / 1024).toStringAsFixed(1)} KB';
    return '${(size / 1024 / 1024).toStringAsFixed(1)} MB';
  }
}

class AppEntry {
  final String name;
  final String? iconBase64;
  final String exePath;
  AppEntry({required this.name, this.iconBase64, required this.exePath});
}

class CustomCommand {
  final String label;
  final String command;
  CustomCommand({required this.label, required this.command});
}

class LogEntry {
  final String time;
  final String device;
  final String action;
  LogEntry({required this.time, required this.device, required this.action});
}

class PcConnection {
  final String deviceId;

  final _statusController = StreamController<ConnectionStatus>.broadcast();
  Stream<ConnectionStatus> get statusStream => _statusController.stream;

  final ValueNotifier<ConnectionStatus> statusNotifier = ValueNotifier(ConnectionStatus.disconnected);
  ConnectionStatus get currentStatus => statusNotifier.value;

  final ValueNotifier<List<String>> clipboardHistoryNotifier = ValueNotifier([]);
  final ValueNotifier<Map<String, FileTransferProgress>> activeTransfersNotifier = ValueNotifier({});
  final ValueNotifier<List<RemoteFile>> recentFilesNotifier = ValueNotifier([]);
  final ValueNotifier<List<AppEntry>> appListNotifier = ValueNotifier([]);
  final ValueNotifier<List<CustomCommand>> commandListNotifier = ValueNotifier([]);
  final ValueNotifier<List<LogEntry>> logEntriesNotifier = ValueNotifier([]);
  final ValueNotifier<Uint8List?> screenFrameNotifier = ValueNotifier(null);

  RTCPeerConnection? _rtcPeer;
  RTCVideoRenderer? _rtcRenderer;
  RTCDataChannel? _inputChannel;
  InputChannel? inputChannel;
  final ValueNotifier<RTCVideoRenderer?> webrtcRendererNotifier = ValueNotifier(null);
  bool _webrtcActive = false;
  Timer? _webrtcTimeout;
  bool _captureActive = false;
  Timer? _webrtcRetryTimer;
  bool _isRetryingWebRtc = false;

  WebSocketChannel? _channel;
  StreamSubscription? _sub;

  String? _host;
  int? _port;
  int? _wssPort;
  String? _token;
  String? _lastClipboardContent;
  bool _disposed = false;

  Uint8List? _sessionNonceBytes;
  Uint8List? _integrityKeyBytes;
  int _cmdSeq = 0;
  String? _lastTransportTrace;

  Timer? _reconnectTimer;
  Timer? _handshakeTimer;
  int _reconnectDelayMs = 500;
  bool _needsPairing = false;

  Completer<Map<String, dynamic>>? _diagCompleter;
  Completer<String?>? _pairCompleter;
  int _screenFrameGen = 0;

  bool get isCaptureActive => _captureActive;
  int lastCaptureIntervalMs = 1000;
  int lastCaptureWidth = 720;
  int lastCaptureQuality = 65;

  PcConnection({required this.deviceId});

  void _setStatus(ConnectionStatus s) {
    if (_disposed) return;
    statusNotifier.value = s;
    if (!_statusController.isClosed) {
      _statusController.add(s);
    }
  }

  ConnectionStatus _connectionStatusFromHelloAck(Map<String, dynamic> obj) {
    final modesRaw = obj['screenStreamModes'];
    final modes = <String>[];
    if (modesRaw is List) {
      for (final item in modesRaw) {
        if (item is String && item.isNotEmpty) {
          modes.add(item);
        }
      }
    }
    return ConnectionStatus(
      connected: true,
      needsPairing: false,
      pcName: obj['pcName'] as String?,
      role: obj['role'] as String?,
      screenStream: obj['screenStream'] as String?,
      screenStreamModes: modes,
    );
  }

  Future<void> connect({required String host, required int port, required String? token, int? wssPort}) async {
    final parsed = PcWebSocket.parseHostInput(host, defaultWsPort: port);
    _host = parsed.host.isNotEmpty ? parsed.host : host.trim();
    _port = parsed.wsPort ?? port;
    _wssPort = wssPort;
    _token = token;
    await _connectInternal();
  }

  Future<void> _connectInternal() async {
    _reconnectTimer?.cancel();
    _handshakeTimer?.cancel();
    final host = _host;
    final port = _port;
    if (host == null || port == null) return;

    _integrityKeyBytes = null;
    _cmdSeq = 0;

    try {
      await _sub?.cancel();
      try {
        await _channel?.sink.close();
      } catch (_) {}
      _channel = null;

      final channel = await PcWebSocket.connectPreferred(
        host: host,
        wsPort: port,
        wssPort: _wssPort ?? kDefaultWssPort,
        preferTls: !kIsWeb && Platform.isAndroid,
        onTrace: (t, d) => _lastTransportTrace = '$t $d',
      );
      if (channel == null) {
        final hint = _lastTransportTrace ?? 'no route to PC (check IP, Wi‑Fi, firewall ports 47821/47824)';
        _scheduleReconnect('Connect failed ($hint)');
        return;
      }
      _channel = channel;
      _sub = channel.stream.listen(
        (event) => unawaited(_onMessage(event)),
        onError: (e) => _scheduleReconnect('WebSocket error: $e'),
        onDone: () => _scheduleReconnect('Disconnected'),
        cancelOnError: true,
      );

      // Start handshake timeout timer (8 seconds)
      _handshakeTimer = Timer(const Duration(seconds: 8), () {
        if (!_disposed && !currentStatus.connected) {
          _scheduleReconnect('Handshake timeout');
        }
      });

      _send({
        'v': 1,
        'type': 'hello',
        'proto': kClientProto,
        'clientVersion': kClientVersion,
        'deviceId': deviceId,
        'screenStreamModes': ScreenStreamModes.clientPreference(),
        if (_token != null) 'token': _token,
      });
    } catch (e) {
      _scheduleReconnect('Connect failed: $e');
    }
  }

  Future<void> _armIntegrityKey() async {
    _integrityKeyBytes = null;
    final tok = _token;
    final nonce = _sessionNonceBytes;
    if (tok == null || nonce == null) return;
    final tb = SessionCrypto.parseTokenHex(tok);
    if (tb == null) return;
    try {
      _integrityKeyBytes = SessionCrypto.deriveIntegrityKeyBytes(tb, nonce);
    } catch (_) {}
  }

  Map<String, dynamic> _withMac(String canon, Map<String, dynamic> payload) {
    final k = _integrityKeyBytes;
    if (k == null) return payload;
    _cmdSeq++;
    final mac = SessionCrypto.commandMacSync(k, _cmdSeq, canon);
    return {...payload, 'cmdSeq': _cmdSeq, 'cmdMac': mac};
  }

  Future<void> _onMessage(dynamic event) async {
    try {
      // jpeg-bin-v1: binary WebSocket frame with 9-byte header
      if (event is List<int>) {
        if (event.length >= 9 && event[0] == 0x01) {
          final bytes = event is Uint8List ? event : Uint8List.fromList(event);
          // Parse width (uint32 big-endian, bytes 1-4)
          // Parse height (uint32 big-endian, bytes 5-8)
          // Remaining bytes (9+) are raw JPEG payload
          final jpegBytes = Uint8List.sublistView(bytes, 9);
          screenFrameNotifier.value = jpegBytes;
        }
        return;
      }

      final obj = jsonDecode(event as String) as Map<String, dynamic>;
      final type = obj['type'];

      switch (type) {
        case 'welcome':
          _sessionNonceBytes = SessionCrypto.parseSessionNonce(obj['sessionNonce'] as String?);
          break;

        case 'helloAck':
          _handshakeTimer?.cancel();
          _handshakeTimer = null;
          await _armIntegrityKey();
          _reconnectDelayMs = 500;
          _needsPairing = false;
          _setStatus(_connectionStatusFromHelloAck(obj));
          break;

        case 'authRequired':
          _handshakeTimer?.cancel();
          _handshakeTimer = null;
          _needsPairing = true;
          _setStatus(const ConnectionStatus(connected: false, needsPairing: true));
          break;

        case 'paired':
          final token = obj['token'] as String?;
          if (token != null) _token = token;
          await _armIntegrityKey();
          _needsPairing = false;
          // Complete any pending pair() call
          if (_pairCompleter != null && !_pairCompleter!.isCompleted) {
            _pairCompleter!.complete(_token);
          }
          break;

        case 'clipboardUpdate':
          try {
            final data = obj['data'] as String?;
            if (data != null && data.isNotEmpty) {
              final bytes = base64Decode(data);
              final text = utf8.decode(bytes);
              _lastClipboardContent = text;
              final history = clipboardHistoryNotifier.value;
              if (!history.contains(text)) {
                clipboardHistoryNotifier.value = [text, ...history.take(9)];
              }
            }
          } catch (_) {}
          break;

        case 'recentFilesList':
          try {
            final files = obj['files'] as List<dynamic>?;
            if (files != null) {
              recentFilesNotifier.value = files.map((f) => RemoteFile(
                path: f['path'] as String? ?? '',
                name: f['name'] as String? ?? '',
                modified: (f['modified'] as num?)?.toInt() ?? 0,
                size: (f['size'] as num?)?.toInt() ?? 0,
              )).toList();
            }
          } catch (_) {}
          break;

        case 'screenFrame':
          try {
            final data = obj['data'] as String?;
            if (data != null) {
              final gen = ++_screenFrameGen;
              final decoded = await compute(_decodeScreenFrameBase64, data);
              if (gen == _screenFrameGen && decoded != null) {
                screenFrameNotifier.value = decoded;
              }
            }
          } catch (_) {}
          break;

        case 'appList':
          try {
            final apps = obj['apps'] as List<dynamic>?;
            if (apps != null) {
              appListNotifier.value = apps.map((a) => AppEntry(
                name: a['name'] as String? ?? '',
                iconBase64: a['iconBase64'] as String?,
                exePath: a['exePath'] as String? ?? '',
              )).toList();
            }
          } catch (_) {}
          break;

        case 'commandList':
          try {
            final cmds = obj['commands'] as List<dynamic>?;
            if (cmds != null) {
              commandListNotifier.value = cmds.map((c) => CustomCommand(
                label: c['label'] as String? ?? '',
                command: c['command'] as String? ?? '',
              )).toList();
            }
          } catch (_) {}
          break;

        case 'logEntries':
          try {
            final entries = obj['entries'] as List<dynamic>?;
            if (entries != null) {
              logEntriesNotifier.value = entries.map((e) => LogEntry(
                time: e['time'] as String? ?? '',
                device: e['device'] as String? ?? '',
                action: e['action'] as String? ?? '',
              )).toList();
            }
          } catch (_) {}
          break;

        case 'notification':
          final notifAppName = obj['appName'] as String? ?? '';
          final notifTitle = obj['title'] as String? ?? '';
          final notifBody = obj['body'] as String? ?? '';
          showMirroredNotification(
            appName: notifAppName,
            title: notifTitle,
            body: notifBody,
          );
          _onNotification?.call(notifTitle, notifBody, notifAppName);
          break;

        case 'networkDiagnostics':
          if (_diagCompleter != null && !_diagCompleter!.isCompleted) {
            _diagCompleter!.complete(obj);
          }
          break;

        case 'uipiBlocked':
          _setStatus(ConnectionStatus(
            connected: currentStatus.connected,
            needsPairing: currentStatus.needsPairing,
            pcName: currentStatus.pcName,
            error: 'Input blocked by Windows User Account Control (UAC). Run Agent as Administrator.',
            role: currentStatus.role,
            screenStream: currentStatus.screenStream,
            screenStreamModes: currentStatus.screenStreamModes,
          ));
          break;

        case 'error':
          _handshakeTimer?.cancel();
          _handshakeTimer = null;
          final msg = obj['message'] as String? ?? 'Unknown error';
          final cur = currentStatus;
          _setStatus(ConnectionStatus(
            connected: cur.connected,
            needsPairing: cur.needsPairing || _needsPairing,
            pcName: cur.pcName,
            error: msg,
            role: cur.role,
            screenStream: cur.screenStream,
            screenStreamModes: cur.screenStreamModes,
          ));
          // Fail any pending pair() call on error
          if (_pairCompleter != null && !_pairCompleter!.isCompleted) {
            _pairCompleter!.complete(null);
          }
          break;

        case 'webrtcAnswer':
          try {
            final sdp = obj['sdp'] as String?;
            final peer = _rtcPeer;
            if (sdp != null && peer != null) {
              await peer.setRemoteDescription(RTCSessionDescription(sdp, 'answer'));
            }
          } catch (_) {}
          break;

        case 'webrtcIce':
          try {
            final candidate = obj['candidate'] as String?;
            final sdpMid = obj['sdpMid'] as String?;
            final sdpMLineIndex = (obj['sdpMLineIndex'] as num?)?.toInt() ?? 0;
            final peer = _rtcPeer;
            if (candidate != null && peer != null) {
              await peer.addCandidate(RTCIceCandidate(candidate, sdpMid, sdpMLineIndex));
            }
          } catch (_) {}
          break;

        case 'webrtcReady':
          _webrtcTimeout?.cancel();
          _webrtcTimeout = null;
          _webrtcActive = true;
          _cancelWebRtcRetry();
          break;

        case 'webrtcFallback':
          await _fallbackFromWebRtc();
          _send({'v': 1, 'type': 'screenCaptureStart', 'intervalMs': 800, 'width': 1080, 'quality': 70});
          break;
      }
    } catch (e) {
      _setStatus(ConnectionStatus(
        connected: currentStatus.connected,
        needsPairing: currentStatus.needsPairing || _needsPairing,
        pcName: currentStatus.pcName,
        error: 'Invalid message from PC',
        role: currentStatus.role,
        screenStream: currentStatus.screenStream,
        screenStreamModes: currentStatus.screenStreamModes,
      ));
    }
  }

  String? get lastTransportTrace => _lastTransportTrace;

  Future<Map<String, dynamic>?> fetchNetworkDiagnostics() async {
    if (!currentStatus.connected) return null;
    final c = Completer<Map<String, dynamic>>();
    _diagCompleter = c;
    _send({'v': 1, 'type': 'networkDiagnostics'});
    try {
      return await c.future.timeout(const Duration(seconds: 8));
    } catch (_) {
      return null;
    } finally {
      if (_diagCompleter == c) {
        _diagCompleter = null;
      }
    }
  }

  // Notification callback
  void Function(String title, String body, String appName)? _onNotification;
  set onNotification(void Function(String title, String body, String appName)? cb) {
    _onNotification = cb;
  }

  Future<String?> pair({required String code, required String deviceName}) async {
    final c = Completer<String?>();
    _pairCompleter = c;
    _send({
      'v': 1,
      'type': 'pair',
      'proto': kClientProto,
      'clientVersion': kClientVersion,
      'deviceId': deviceId,
      'deviceName': deviceName,
      'code': code,
      'screenStreamModes': ScreenStreamModes.clientPreference(),
    });
    try {
      return await c.future.timeout(const Duration(seconds: 5));
    } catch (_) {
      return null;
    } finally {
      if (_pairCompleter == c) _pairCompleter = null;
    }
  }

  // ── Input Control ──
  void lockPc() => _send({'v': 1, 'type': 'lock'});

  void sendInput({required int backspaces, required String text}) {
    _send({'v': 1, 'type': 'input', 'backspaces': backspaces, 'text': text});
  }

  String _lastKeyboardText = '';
  String get lastKeyboardText => _lastKeyboardText;

  void resetKeyboardText({String value = ''}) {
    _lastKeyboardText = value;
  }

  static const int _replaceAllBackspaceThreshold = 5;

  bool isReplaceAll(TextDiff diff, String oldText) {
    if (diff.backspaces >= _replaceAllBackspaceThreshold) {
      return true;
    }
    if (diff.backspaces == oldText.length && oldText.isNotEmpty) {
      return true;
    }
    return false;
  }

  void sendReplaceAllText({required String text}) {
    _send({'v': 1, 'type': 'replaceAllText', 'text': text});
  }

  void launchApp(String command, {List<String>? args}) {
    final argCanon = (args == null || args.isEmpty) ? '' : args.join('\x1e');
    _send(_withMac('launch|$command|$argCanon', {'v': 1, 'type': 'launch', 'command': command, if (args != null) 'args': args}));
  }

  void launchAppByPath(String exePath) {
    _send(_withMac('launchapp|$exePath', {'v': 1, 'type': 'launchApp', 'exePath': exePath}));
  }

  void mouseMove({required int dx, required int dy}) {
    if (dx == 0 && dy == 0) return;
    final ic = inputChannel;
    if (ic != null) {
      ic.sendMouseMove(dx, dy);
      return;
    }
    _send({'v': 1, 'type': 'mouseMove', 'dx': dx, 'dy': dy});
  }

  void mouseScroll({required int dy}) {
    if (dy == 0) return;
    final ic = inputChannel;
    if (ic != null) {
      ic.sendScroll(dy);
      return;
    }
    _send({'v': 1, 'type': 'mouseScroll', 'dy': dy});
  }

  void mouseButton({required String button, required String action}) {
    final ic = inputChannel;
    if (ic != null) {
      final btnMap = const {'left': 0, 'right': 1, 'middle': 2};
      final btnCode = btnMap[button];
      if (btnCode != null) {
        if (action == 'down') {
          ic.sendButtonDown(btnCode);
        } else if (action == 'up') {
          ic.sendButtonUp(btnCode);
        } else if (action == 'click') {
          ic.sendButtonDown(btnCode);
          ic.sendButtonUp(btnCode);
        }
      }
      return;
    }
    _send({'v': 1, 'type': 'mouseButton', 'button': button, 'action': action});
  }

  void keyPress({required int vk, bool extended = false}) {
    final ic = inputChannel;
    if (ic != null) {
      ic.sendKey(vk, 0, extended ? 1 : 0);
      return;
    }
    _send({'v': 1, 'type': 'key', 'vk': vk, 'action': 'press', if (extended) 'extended': true});
  }

  void keyDown({required int vk, bool extended = false}) {
    final ic = inputChannel;
    if (ic != null) {
      ic.sendKey(vk, 1, extended ? 1 : 0);
      return;
    }
    _send({'v': 1, 'type': 'key', 'vk': vk, 'action': 'down', if (extended) 'extended': true});
  }

  void keyUp({required int vk, bool extended = false}) {
    final ic = inputChannel;
    if (ic != null) {
      ic.sendKey(vk, 2, extended ? 1 : 0);
      return;
    }
    _send({'v': 1, 'type': 'key', 'vk': vk, 'action': 'up', if (extended) 'extended': true});
  }

  void keyCombo(List<String> keys) {
    _send({'v': 1, 'type': 'keyCombo', 'keys': keys});
  }

  void mediaKey(String key) {
    _send({'v': 1, 'type': 'mediaKey', 'key': key});
  }

  void setVolume({required int level}) {
    _send({'v': 1, 'type': 'setVolume', 'level': level.clamp(0, 100)});
  }

  void setBrightness({required int level}) {
    _send({'v': 1, 'type': 'setBrightness', 'level': level.clamp(0, 100)});
  }

  void shutdownPc({required String password}) {
    _send(_withMac('shutdown|$password', {'v': 1, 'type': 'shutdown', 'password': password}));
  }

  // ── Clipboard ──
  void setClipboard({required String text}) {
    if (text == _lastClipboardContent) return;
    _lastClipboardContent = text;
    final encoded = base64Encode(utf8.encode(text));
    _send({'v': 1, 'type': 'clipboardSet', 'data': encoded, 'format': 'text/plain'});
  }

  // ── Screen Capture ──
  void startScreenCapture({int intervalMs = 1000, int width = 720, int quality = 65}) {
    _captureActive = true;
    lastCaptureIntervalMs = intervalMs;
    lastCaptureWidth = width;
    lastCaptureQuality = quality;
    final mode = currentStatus.effectiveScreenStream;
    if (mode == ScreenStreamModes.webRtcV1) {
      _startWebRtc(width: width, quality: quality);
      return;
    }
    if (mode != ScreenStreamModes.jpegV1 && mode != ScreenStreamModes.jpegBinV1) return;
    _send({'v': 1, 'type': 'screenCaptureStart', 'intervalMs': intervalMs, 'width': width, 'quality': quality});
  }

  void stopScreenCapture() {
    _captureActive = false;
    _cancelWebRtcRetry();
    if (_webrtcActive) {
      _fallbackFromWebRtc(rescheduleRetry: false);
    }
    _send({'v': 1, 'type': 'screenCaptureStop'});
    _screenFrameGen++;
    screenFrameNotifier.value = null;
  }

  Future<void> _startWebRtc({int width = 720, int quality = 65}) async {
    try {
      await _fallbackFromWebRtc(rescheduleRetry: false);

      final renderer = RTCVideoRenderer();
      await renderer.initialize();
      _rtcRenderer = renderer;

      final peer = await createPeerConnection({
        'iceServers': [],
        'sdpSemantics': 'unified-plan',
      });
      _rtcPeer = peer;

      peer.onTrack = (event) {
        if (event.track.kind == 'video') {
          renderer.srcObject = event.streams[0];
          webrtcRendererNotifier.value = renderer;
        }
      };

      peer.onIceCandidate = (candidate) {
        _send({
          'v': 1,
          'type': 'webrtcIce',
          'candidate': candidate.candidate,
          'sdpMid': candidate.sdpMid,
          'sdpMLineIndex': candidate.sdpMLineIndex,
        });
      };

      peer.onConnectionState = (state) {
        if (state == RTCPeerConnectionState.RTCPeerConnectionStateFailed ||
            state == RTCPeerConnectionState.RTCPeerConnectionStateDisconnected) {
          _fallbackFromWebRtc(rescheduleRetry: true);
        }
      };

      peer.onIceConnectionState = (state) {
        if (state == RTCIceConnectionState.RTCIceConnectionStateFailed ||
            state == RTCIceConnectionState.RTCIceConnectionStateDisconnected) {
          _fallbackFromWebRtc(rescheduleRetry: true);
        }
      };

      final dcInit = RTCDataChannelInit()
        ..ordered = false
        ..maxRetransmits = 0;
      
      final channel = await peer.createDataChannel('input', dcInit);
      _inputChannel = channel;
      inputChannel = InputChannel(channel);

      final offer = await peer.createOffer({});
      await peer.setLocalDescription(offer);

      _send({
        'v': 1,
        'type': 'webrtcOffer',
        'sdp': offer.sdp,
        'width': width,
        'quality': quality,
      });

      _webrtcTimeout = Timer(const Duration(seconds: 5), () {
        _fallbackFromWebRtc(rescheduleRetry: true);
      });
    } catch (_) {
      await _fallbackFromWebRtc(rescheduleRetry: true);
    }
  }

  Future<void> _fallbackFromWebRtc({bool rescheduleRetry = true}) async {
    if (_channel != null) {
      _send({'v': 1, 'type': 'webrtcFailed'});
    }
    _webrtcTimeout?.cancel();
    _webrtcTimeout = null;

    final renderer = _rtcRenderer;
    _rtcRenderer = null;
    webrtcRendererNotifier.value = null;

    final peer = _rtcPeer;
    _rtcPeer = null;

    final channel = _inputChannel;
    _inputChannel = null;
    inputChannel = null;

    _webrtcActive = false;

    if (channel != null) {
      unawaited(channel.close());
    }
    if (peer != null) {
      unawaited(peer.close());
    }
    if (renderer != null) {
      unawaited(renderer.dispose());
    }

    if (rescheduleRetry && _captureActive && !_disposed && currentStatus.effectiveScreenStream == ScreenStreamModes.webRtcV1) {
      _scheduleWebRtcRetry();
    }
  }

  void _scheduleWebRtcRetry() {
    _webrtcRetryTimer?.cancel();
    _webrtcRetryTimer = Timer(const Duration(seconds: 15), () async {
      if (!_captureActive || _disposed || _webrtcActive || _isRetryingWebRtc) return;
      _isRetryingWebRtc = true;
      try {
        await _startWebRtc(width: 720, quality: 65);
      } catch (_) {
        // Fallback is called automatically inside _startWebRtc on failure, rescheduling the timer.
      } finally {
        _isRetryingWebRtc = false;
      }
    });
  }

  void _cancelWebRtcRetry() {
    _webrtcRetryTimer?.cancel();
    _webrtcRetryTimer = null;
  }

  // ── App List ──
  void requestAppList() {
    _send({'v': 1, 'type': 'getAppList'});
  }

  // ── Custom Commands ──
  void requestCommands() {
    _send({'v': 1, 'type': 'getCommands'});
  }

  void runCommand(int index) {
    _send(_withMac('runcommand|$index', {'v': 1, 'type': 'runCommand', 'index': index}));
  }

  // ── Settings ──
  void settingsSync({required bool autoLockOnDisconnect}) {
    _send(_withMac('settingsSync|$deviceId|$autoLockOnDisconnect', {
      'v': 1,
      'type': 'settingsSync',
      'autoLockOnDisconnect': autoLockOnDisconnect,
    }));
  }

  // ── Audit Log ──
  void requestLogs(String date) {
    _send({'v': 1, 'type': 'getLogs', 'date': date});
  }

  // ── File Transfer ──
  void requestRecentFiles({int limit = 20}) {
    _send({'v': 1, 'type': 'listRecentFiles', 'limit': limit});
  }

  Future<void> uploadFile(String filePath, {required Function(FileTransferProgress) onProgress}) async {
    try {
      final file = File(filePath);
      if (!await file.exists()) return;
      final fileSize = await file.length();
      if (fileSize <= 0) return;
      // Use Uri to correctly extract filename on both Windows and Unix paths
      final filename = file.uri.pathSegments.last;
      final transferId = const Uuid().v4();

      final progress = FileTransferProgress(filename: filename, totalBytes: fileSize, isDownload: false);
      activeTransfersNotifier.value = {...activeTransfersNotifier.value, transferId: progress};

      _send({'v': 1, 'type': 'fileTransferStart', 'id': transferId, 'filename': filename, 'size': fileSize, 'direction': 'upload'});
      await Future<void>.delayed(const Duration(milliseconds: 200));

      // Stream chunks from disk instead of reading entire file into memory
      const chunkSize = 50 * 1024;
      final totalChunks = (fileSize / chunkSize).ceil();
      final raf = await file.open(mode: FileMode.read);
      try {
        for (int i = 0; i < totalChunks; i++) {
          final chunk = await raf.read(chunkSize);
          if (chunk.isEmpty) break;
          _send({'v': 1, 'type': 'fileTransferChunk', 'id': transferId, 'chunkIndex': i, 'totalChunks': totalChunks, 'data': base64Encode(chunk), 'size': chunk.length});
          progress.transferredBytes += chunk.length;
          onProgress(progress);
          activeTransfersNotifier.value = {...activeTransfersNotifier.value, transferId: progress};
          await Future<void>.delayed(const Duration(milliseconds: 10));
        }
      } finally {
        await raf.close();
      }

      _send({'v': 1, 'type': 'fileTransferComplete', 'id': transferId});
      await Future<void>.delayed(const Duration(milliseconds: 500));
      final updated = Map<String, FileTransferProgress>.from(activeTransfersNotifier.value);
      updated.remove(transferId);
      activeTransfersNotifier.value = updated;
    } catch (_) {}
  }

  void _send(Map<String, dynamic> obj) {
    if (_disposed) return;
    final ch = _channel;
    if (ch == null) { _scheduleReconnect('Not connected'); return; }
    try { ch.sink.add(jsonEncode(obj)); }
    catch (e) { _scheduleReconnect('Send failed: $e'); }
  }

  void _scheduleReconnect(String reason) {
    if (_disposed) return;
    final preservePairing = _needsPairing || currentStatus.needsPairing;
    _setStatus(ConnectionStatus(
      connected: false,
      needsPairing: preservePairing,
      error: preservePairing ? null : reason,
      pcName: currentStatus.pcName,
      role: currentStatus.role,
      screenStream: currentStatus.screenStream,
      screenStreamModes: currentStatus.screenStreamModes,
    ));
    _reconnectTimer?.cancel();
    _reconnectTimer = null;
    if (preservePairing) {
      return; // Stop automatic reconnects until user explicitly acts
    }
    _reconnectTimer = Timer(Duration(milliseconds: _reconnectDelayMs), () {
      if (_disposed) return;
      // Cap at 30s instead of 5s to reduce battery drain during prolonged outages
      _reconnectDelayMs = (_reconnectDelayMs * 2).clamp(500, 30000);
      unawaited(_connectInternal());
    });
  }

  void dispose() {
    _disposed = true;
    _reconnectTimer?.cancel();
    _handshakeTimer?.cancel();
    _webrtcRetryTimer?.cancel();
    _sub?.cancel();
    _channel?.sink.close();
    _statusController.close();
    statusNotifier.dispose();
    clipboardHistoryNotifier.dispose();
    activeTransfersNotifier.dispose();
    recentFilesNotifier.dispose();
    appListNotifier.dispose();
    commandListNotifier.dispose();
    logEntriesNotifier.dispose();
    screenFrameNotifier.dispose();
    webrtcRendererNotifier.dispose();
    _webrtcTimeout?.cancel();
    _rtcRenderer?.dispose();
    _inputChannel?.close();
    _inputChannel = null;
    inputChannel = null;
    _rtcPeer?.close();
  }
}

class DiscoveryClient {
  static Future<List<DiscoveredPc>> discover({required Duration timeout}) async {
    final RawDatagramSocket socket;
    try {
      socket = await RawDatagramSocket.bind(InternetAddress.anyIPv4, 0);
    } catch (e) {
      throw Exception('Failed to bind discovery socket: $e');
    }
    socket.broadcastEnabled = true;
    final results = <DiscoveredPc>[];
    final seen = <String>{};
    final probeBytes = utf8.encode(kDiscoverProbe);

    socket.listen((event) {
      if (event != RawSocketEvent.read) return;
      final dg = socket.receive();
      if (dg == null) return;
      try {
        final obj = jsonDecode(utf8.decode(dg.data)) as Map<String, dynamic>;
        if (obj['type'] != 'discoverResponse') return;
        final name = (obj['pcName'] as String?) ?? dg.address.address;
        final port = (obj['wsPort'] as num?)?.toInt() ?? kWsPortDefault;
        final wssPort = (obj['wssPort'] as num?)?.toInt();
        final key = '${dg.address.address}:$port';
        if (seen.add(key)) {
          results.add(DiscoveredPc(name: name, address: dg.address, wsPort: port, wssPort: wssPort));
        }
      } catch (_) {}
    });

    final targets = <InternetAddress>[InternetAddress('255.255.255.255')];
    try {
      for (final iface in await NetworkInterface.list(type: InternetAddressType.IPv4, includeLinkLocal: false)) {
        for (final addr in iface.addresses) {
          final bcast = _guessBroadcast24(addr.address);
          if (bcast != null && !targets.any((t) => t.address == bcast)) {
            targets.add(InternetAddress(bcast));
          }
        }
      }
    } catch (_) {}

    final end = DateTime.now().add(timeout);
    while (DateTime.now().isBefore(end)) {
      for (final t in targets) {
        try {
          socket.send(probeBytes, t, kDiscoveryPort);
        } catch (_) {}
      }
      await Future<void>.delayed(const Duration(milliseconds: 400));
    }

    socket.close();
    return results;
  }

  /// Typical home Wi‑Fi /24 directed broadcast (best-effort on Android).
  static String? _guessBroadcast24(String ip) {
    final parts = ip.split('.');
    if (parts.length != 4) return null;
    final octets = parts.map(int.tryParse).toList();
    if (octets.any((o) => o == null || o < 0 || o > 255)) return null;
    final a = octets[0]!;
    if (a == 192 && octets[1] == 168) {
      return '192.168.${octets[2]}.255';
    }
    if (a == 10) {
      return '10.${octets[1]}.${octets[2]}.255';
    }
    if (a == 172 && octets[1]! >= 16 && octets[1]! <= 31) {
      return '172.${octets[1]}.${octets[2]}.255';
    }
    return null;
  }
}

class TextDiff {
  final int backspaces;
  final String inserted;
  TextDiff(this.backspaces, this.inserted);

  static TextDiff compute(String oldText, String newText) {
    if (oldText == newText) return TextDiff(0, '');
    var prefix = 0;
    final minLen = oldText.length < newText.length ? oldText.length : newText.length;
    while (prefix < minLen && oldText.codeUnitAt(prefix) == newText.codeUnitAt(prefix)) {
      prefix++;
    }
    return TextDiff(oldText.substring(prefix).length, newText.substring(prefix));
  }
}

Uint8List? _decodeScreenFrameBase64(String data) {
  try {
    return base64Decode(data);
  } catch (_) {
    return null;
  }
}
