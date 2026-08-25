import 'dart:async';
import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:web_socket_channel/web_socket_channel.dart';

// ── Voice Agent Connection Status ──

enum VoiceAgentConnectionState { disconnected, connecting, authenticating, connected, authFailed }

class VoiceAgentStatus {
  final VoiceAgentConnectionState state;
  final String? error;

  const VoiceAgentStatus({required this.state, this.error});

  static const disconnected = VoiceAgentStatus(state: VoiceAgentConnectionState.disconnected);

  bool get connected => state == VoiceAgentConnectionState.connected;
  bool get isAuthFailed => state == VoiceAgentConnectionState.authFailed;
}

// ── Tool Call Result ──

class ToolCallResult {
  final bool ok;
  final String message;
  ToolCallResult({required this.ok, required this.message});
}

// ── Preferences Keys ──

const String _kAgentAddress = 'voice_agent_address';
const String _kAgentToken = 'voice_agent_token';

// ── Voice Agent WebSocket Service ──

class VoiceAgentService {
  final ValueNotifier<VoiceAgentStatus> statusNotifier = ValueNotifier(VoiceAgentStatus.disconnected);
  final _statusController = StreamController<VoiceAgentStatus>.broadcast();
  Stream<VoiceAgentStatus> get statusStream => _statusController.stream;
  VoiceAgentStatus get currentStatus => statusNotifier.value;

  WebSocketChannel? _channel;
  StreamSubscription? _sub;
  Timer? _reconnectTimer;
  int _reconnectDelayMs = 500;
  bool _disposed = false;

  String? _address;
  String? _token;

  /// Cached tool schemas fetched from the agent via `list_tools`.
  List<Map<String, dynamic>>? _cachedTools;
  List<Map<String, dynamic>>? get cachedTools => _cachedTools;

  Completer<bool>? _authCompleter;
  Completer<List<Map<String, dynamic>>>? _toolsCompleter;
  Completer<ToolCallResult>? _callToolCompleter;

  VoiceAgentService();

  void _setStatus(VoiceAgentStatus s) {
    if (_disposed) return;
    statusNotifier.value = s;
    if (!_statusController.isClosed) {
      _statusController.add(s);
    }
  }

  // ── Load / Save Settings ──

  Future<void> loadSettings() async {
    final prefs = await SharedPreferences.getInstance();
    _address = prefs.getString(_kAgentAddress);
    _token = prefs.getString(_kAgentToken);
  }

  Future<void> saveSettings({required String address, required String token}) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString(_kAgentAddress, address);
    await prefs.setString(_kAgentToken, token);
    _address = address;
    _token = token;
  }

  String? get address => _address;
  String? get token => _token;
  bool get isConfigured => _address != null && _address!.isNotEmpty && _token != null && _token!.isNotEmpty;

  // ── Connect ──

  Future<void> connect({String? address, String? token}) async {
    final addr = address ?? _address;
    final tok = token ?? _token;
    if (addr == null || addr.isEmpty || tok == null || tok.isEmpty) {
      _setStatus(const VoiceAgentStatus(
        state: VoiceAgentConnectionState.disconnected,
        error: 'Address and token are required',
      ));
      return;
    }
    _address = addr;
    _token = tok;
    await _connectInternal();
  }

  Future<void> _connectInternal() async {
    _reconnectTimer?.cancel();
    final addr = _address;
    final tok = _token;
    if (addr == null || tok == null) return;

    _setStatus(const VoiceAgentStatus(state: VoiceAgentConnectionState.connecting));

    try {
      await _sub?.cancel();
      try { await _channel?.sink.close(); } catch (_) {}
      _channel = null;

      final uri = Uri.parse(addr.startsWith('ws://') || addr.startsWith('wss://') ? addr : 'ws://$addr');
      final channel = WebSocketChannel.connect(uri);
      await channel.ready.timeout(const Duration(seconds: 4));
      _channel = channel;

      _sub = channel.stream.listen(
        (event) => unawaited(_onMessage(event)),
        onError: (e) => _scheduleReconnect('WebSocket error: $e'),
        onDone: () => _scheduleReconnect('Disconnected'),
        cancelOnError: true,
      );

      // Send auth
      _setStatus(const VoiceAgentStatus(state: VoiceAgentConnectionState.authenticating));
      _authCompleter = Completer<bool>();
      _send({'type': 'auth', 'token': tok});

      final authed = await _authCompleter!.future.timeout(const Duration(seconds: 4), onTimeout: () => false);
      _authCompleter = null;

      if (!authed) {
        _setStatus(const VoiceAgentStatus(
          state: VoiceAgentConnectionState.authFailed,
          error: 'Authentication failed — check your token',
        ));
        return;
      }

      _reconnectDelayMs = 500;
      _setStatus(const VoiceAgentStatus(state: VoiceAgentConnectionState.connected));

      // Pre-fetch tool list
      unawaited(_fetchTools());
    } catch (e) {
      _scheduleReconnect('Connect failed: $e');
    }
  }

  Future<void> _onMessage(dynamic event) async {
    try {
      final obj = jsonDecode(event as String) as Map<String, dynamic>;
      final type = obj['type'];

      switch (type) {
        case 'auth_ok':
          if (_authCompleter != null && !_authCompleter!.isCompleted) {
            _authCompleter!.complete(true);
          }
          break;

        case 'error':
          final msg = obj['message'] as String? ?? 'Unknown error';
          // If auth was in progress, fail it
          if (_authCompleter != null && !_authCompleter!.isCompleted) {
            _authCompleter!.complete(false);
          }
          // If a tool call was in progress, fail it
          if (_callToolCompleter != null && !_callToolCompleter!.isCompleted) {
            _callToolCompleter!.complete(ToolCallResult(ok: false, message: msg));
          }
          break;

        case 'tools':
          final rawTools = obj['tools'] as List<dynamic>? ?? [];
          _cachedTools = rawTools.cast<Map<String, dynamic>>();
          if (_toolsCompleter != null && !_toolsCompleter!.isCompleted) {
            _toolsCompleter!.complete(_cachedTools!);
          }
          break;

        case 'tool_result':
          final ok = obj['ok'] == true;
          final message = obj['message'] as String? ?? (ok ? 'Done' : 'Failed');
          if (_callToolCompleter != null && !_callToolCompleter!.isCompleted) {
            _callToolCompleter!.complete(ToolCallResult(ok: ok, message: message));
          }
          break;
      }
    } catch (e) {
      debugPrint('VoiceAgentService: malformed message: $e');
    }
  }

  // ── List Tools ──

  Future<List<Map<String, dynamic>>> listTools() async {
    if (!currentStatus.connected) return _cachedTools ?? [];
    final c = Completer<List<Map<String, dynamic>>>();
    _toolsCompleter = c;
    _send({'type': 'list_tools'});
    try {
      final tools = await c.future.timeout(const Duration(seconds: 4));
      return tools;
    } catch (_) {
      return _cachedTools ?? [];
    } finally {
      if (_toolsCompleter == c) _toolsCompleter = null;
    }
  }

  Future<void> _fetchTools() async {
    try {
      await listTools();
    } catch (_) {}
  }

  /// Validate a tool call against the cached tools schemas fetched via `list_tools`.
  /// Returns `true` if valid or if no tool schemas are cached to check against.
  /// Returns `false` if the tool is not defined or an enum argument value is not whitelisted.
  bool validateToolCall(String toolName, Map<String, dynamic> args) {
    final tools = _cachedTools;
    if (tools == null || tools.isEmpty) return true;

    Map<String, dynamic>? matchedTool;
    for (final t in tools) {
      final name = t['name'] ?? t['function']?['name'];
      if (name == toolName) {
        matchedTool = t;
        break;
      }
    }

    if (matchedTool == null) return false;

    // Check parameter enums if specified in schema
    final fn = matchedTool['function'] is Map<String, dynamic>
        ? matchedTool['function'] as Map<String, dynamic>
        : matchedTool;
    final params = fn['parameters'] as Map<String, dynamic>?;
    final props = params?['properties'] as Map<String, dynamic>?;

    if (props != null) {
      for (final entry in args.entries) {
        final propName = entry.key;
        final propVal = entry.value?.toString().toLowerCase();
        final propSchema = props[propName] as Map<String, dynamic>?;
        final enumList = propSchema?['enum'] as List<dynamic>?;
        if (enumList != null && propVal != null) {
          final allowed = enumList.map((e) => e.toString().toLowerCase()).toSet();
          if (!allowed.contains(propVal)) {
            return false;
          }
        }
      }
    }

    return true;
  }

  // ── Call Tool ──

  Future<ToolCallResult> callTool({required String tool, required Map<String, dynamic> args}) async {
    if (!currentStatus.connected) {
      return ToolCallResult(
        ok: false,
        message: "Check you're on the same WiFi and the PC agent is running",
      );
    }
    final c = Completer<ToolCallResult>();
    _callToolCompleter = c;
    _send({'type': 'call_tool', 'tool': tool, 'args': args});
    try {
      return await c.future.timeout(const Duration(seconds: 4), onTimeout: () {
        return ToolCallResult(
          ok: false,
          message: "Check you're on the same WiFi and the PC agent is running",
        );
      });
    } finally {
      if (_callToolCompleter == c) _callToolCompleter = null;
    }
  }

  // ── Test Connection (one-shot, no auto-reconnect) ──

  Future<VoiceAgentStatus> testConnection({required String address, required String token}) async {
    WebSocketChannel? testChannel;
    StreamSubscription? testSub;
    try {
      final uri = Uri.parse(
        address.startsWith('ws://') || address.startsWith('wss://') ? address : 'ws://$address',
      );
      testChannel = WebSocketChannel.connect(uri);
      await testChannel.ready.timeout(const Duration(seconds: 4));

      final authCompleter = Completer<bool>();
      testSub = testChannel.stream.listen((event) {
        try {
          final obj = jsonDecode(event as String) as Map<String, dynamic>;
          if (obj['type'] == 'auth_ok' && !authCompleter.isCompleted) {
            authCompleter.complete(true);
          } else if (obj['type'] == 'error' && !authCompleter.isCompleted) {
            authCompleter.complete(false);
          }
        } catch (_) {
          if (!authCompleter.isCompleted) authCompleter.complete(false);
        }
      }, onError: (_) {
        if (!authCompleter.isCompleted) authCompleter.complete(false);
      });

      testChannel.sink.add(jsonEncode({'type': 'auth', 'token': token}));
      final ok = await authCompleter.future.timeout(const Duration(seconds: 4), onTimeout: () => false);

      if (ok) {
        return const VoiceAgentStatus(state: VoiceAgentConnectionState.connected);
      } else {
        return const VoiceAgentStatus(
          state: VoiceAgentConnectionState.authFailed,
          error: 'Authentication failed — check your token',
        );
      }
    } catch (e) {
      return VoiceAgentStatus(
        state: VoiceAgentConnectionState.disconnected,
        error: "Can't reach agent — check IP, port, and that the agent is running",
      );
    } finally {
      await testSub?.cancel();
      try { await testChannel?.sink.close(); } catch (_) {}
    }
  }

  // ── Transport ──

  void _send(Map<String, dynamic> obj) {
    if (_disposed) return;
    final ch = _channel;
    if (ch == null) { _scheduleReconnect('Not connected'); return; }
    try { ch.sink.add(jsonEncode(obj)); }
    catch (e) { _scheduleReconnect('Send failed: $e'); }
  }

  // ── Reconnect with backoff (matches PcConnection._scheduleReconnect) ──

  void _scheduleReconnect(String reason) {
    if (_disposed) return;
    // Don't auto-reconnect after auth failure — user must fix settings
    if (currentStatus.isAuthFailed) return;
    _setStatus(VoiceAgentStatus(
      state: VoiceAgentConnectionState.disconnected,
      error: reason,
    ));
    _reconnectTimer?.cancel();
    _reconnectTimer = null;
    _reconnectTimer = Timer(Duration(milliseconds: _reconnectDelayMs), () {
      if (_disposed) return;
      // Cap at 30s to reduce battery drain during prolonged outages
      _reconnectDelayMs = (_reconnectDelayMs * 2).clamp(500, 30000);
      unawaited(_connectInternal());
    });
  }

  // ── Disconnect ──

  void disconnect() {
    _reconnectTimer?.cancel();
    _reconnectTimer = null;
    _sub?.cancel();
    _sub = null;
    try { _channel?.sink.close(); } catch (_) {}
    _channel = null;
    _cachedTools = null;
    _setStatus(VoiceAgentStatus.disconnected);
  }

  // ── Dispose ──

  void dispose() {
    _disposed = true;
    _reconnectTimer?.cancel();
    _sub?.cancel();
    try { _channel?.sink.close(); } catch (_) {}
    _statusController.close();
    statusNotifier.dispose();
  }
}
