import 'dart:io';

import 'package:web_socket_channel/io.dart';
import 'package:web_socket_channel/web_socket_channel.dart';

import 'tofu_pin_store.dart';
import 'connection.dart' show kDefaultWssPort;

class PcWebSocket {
  /// WSS first (TOFU), then cleartext WS. [wssPort] defaults to [kDefaultWssPort].
  static Future<WebSocketChannel?> connectPreferred({
    required String host,
    required int wsPort,
    int? wssPort,
    required bool preferTls,
    void Function(String transport, String detail)? onTrace,
  }) async {
    final tlsPort = wssPort ?? kDefaultWssPort;
    if (preferTls) {
      try {
        final client = HttpClient();
        client.badCertificateCallback = (cert, h, p) {
          if (h != host || p != tlsPort) return false;
          return TofuPinStore.verifyServerCertSync(cert, h, p);
        };
        final ws = await WebSocket.connect(
          'wss://$host:$tlsPort/ws',
          customClient: client,
        );
        onTrace?.call('wss', 'ok');
        return IOWebSocketChannel(ws);
      } catch (e) {
        onTrace?.call('wss_fail', '$e');
      }
    }

    // Use dart:io WebSocket.connect() so the connection is properly awaited
    // and errors are caught. IOWebSocketChannel.connect() returns synchronously
    // and never throws, leading to false-positive "connected" states.
    try {
      final ws = await WebSocket.connect('ws://$host:$wsPort/ws');
      onTrace?.call('ws', 'ok');
      return IOWebSocketChannel(ws);
    } catch (e) {
      onTrace?.call('ws_fail', '$e');
      return null;
    }
  }
}
