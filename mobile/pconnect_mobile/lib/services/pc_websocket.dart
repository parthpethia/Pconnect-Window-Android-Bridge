import 'dart:async';
import 'dart:io';

import 'package:web_socket_channel/io.dart';
import 'package:web_socket_channel/web_socket_channel.dart';

import 'tofu_pin_store.dart';
import 'connection.dart' show kDefaultWssPort;

class PcWebSocket {
  static const Duration _connectTimeout = Duration(seconds: 12);

  /// Parses manual host entry or pasted `ws://ip:port/ws` URL.
  static ({String host, int? wsPort}) parseHostInput(String raw, {required int defaultWsPort}) {
    var s = raw.trim();
    if (s.isEmpty) return (host: '', wsPort: null);
    if (!s.contains('://') && s.contains('/') && !s.contains(':')) {
      s = 'http://$s';
    }
    if (s.contains('://')) {
      final u = Uri.tryParse(s);
      if (u != null && u.host.isNotEmpty) {
        return (host: u.host, wsPort: u.hasPort ? u.port : defaultWsPort);
      }
    }
    return (host: s, wsPort: null);
  }

  /// Connects to the PC's WebSocket server.
  /// When [preferTls] is true (Android), tries WSS first for encrypted traffic,
  /// falling back to cleartext WS only if TLS fails.
  /// When [preferTls] is false, tries cleartext WS only.
  static Future<WebSocketChannel?> connectPreferred({
    required String host,
    required int wsPort,
    int? wssPort,
    required bool preferTls,
    void Function(String transport, String detail)? onTrace,
  }) async {
    final parsed = parseHostInput(host, defaultWsPort: wsPort);
    final h = parsed.host;
    final effectiveWsPort = parsed.wsPort ?? wsPort;
    final tlsPort = wssPort ?? kDefaultWssPort;

    if (h.isEmpty) {
      onTrace?.call('connect_fail', 'empty host');
      return null;
    }

    if (preferTls) {
      // Try encrypted WSS first — keeps all traffic (auth tokens, clipboard,
      // screen captures, file data) hidden from LAN sniffers.
      var wssAttempt = await _tryWss(h, tlsPort, onTrace);
      if (wssAttempt.channel == null) {
        // Only clear the pin and retry WSS if the failure is likely due to certificate validation/mismatch.
        // If it was a connection timeout or socket error, retrying is redundant.
        final err = wssAttempt.detail ?? '';
        final isCertError = err.contains('HandshakeException') ||
            err.contains('TlsException') ||
            err.contains('cert');
        if (isCertError) {
          await TofuPinStore.clearPin(h, tlsPort);
          wssAttempt = await _tryWss(h, tlsPort, onTrace);
        }
      }
      if (wssAttempt.channel != null) {
        onTrace?.call('wss', wssAttempt.detail ?? 'ok');
        return wssAttempt.channel;
      }

      // WSS failed — fall back to cleartext WS so the user isn't blocked.
      final wsAttempt = await _tryWs(h, effectiveWsPort, onTrace);
      if (wsAttempt.channel != null) {
        onTrace?.call('ws_fallback', wsAttempt.detail ?? 'ok');
        return wsAttempt.channel;
      }

      onTrace?.call(
        'connect_fail',
        'wss: ${wssAttempt.detail ?? "failed"}; ws: ${wsAttempt.detail ?? "failed"}',
      );
      return null;
    }

    // Non-TLS path: cleartext WS only.
    final wsAttempt = await _tryWs(h, effectiveWsPort, onTrace);
    if (wsAttempt.channel != null) {
      onTrace?.call('ws', wsAttempt.detail ?? 'ok');
      return wsAttempt.channel;
    }

    onTrace?.call('connect_fail', 'ws: ${wsAttempt.detail ?? "failed"}');
    return null;
  }

  static Future<({WebSocketChannel? channel, String? detail})> _tryWss(
    String host,
    int tlsPort,
    void Function(String transport, String detail)? onTrace,
  ) async {
    HttpClient? client;
    try {
      client = HttpClient();
      client.connectionTimeout = _connectTimeout;
      client.badCertificateCallback = (cert, h, p) {
        if (p != tlsPort) return false;
        // Pin to the host the user connected to (callback [h] may differ from URL host).
        return TofuPinStore.verifyServerCertSync(cert, host, tlsPort);
      };
      final ws = await WebSocket.connect(
        'wss://$host:$tlsPort/ws',
        customClient: client,
      ).timeout(_connectTimeout);
      return (channel: IOWebSocketChannel(ws), detail: 'ok');
    } catch (e) {
      onTrace?.call('wss_fail', '$e');
      return (channel: null, detail: '$e');
    } finally {
      // WebSocket owns the connection; do not close [client] here.
    }
  }

  static Future<({WebSocketChannel? channel, String? detail})> _tryWs(
    String host,
    int wsPort,
    void Function(String transport, String detail)? onTrace,
  ) async {
    try {
      final ws = await WebSocket.connect('ws://$host:$wsPort/ws').timeout(_connectTimeout);
      return (channel: IOWebSocketChannel(ws), detail: 'ok');
    } catch (e) {
      onTrace?.call('ws_fail', '$e');
      return (channel: null, detail: '$e');
    }
  }
}
