import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:pconnect_mobile/services/voice_agent_service.dart';

void main() {
  group('VoiceAgentService Reconnect Tests', () {
    late HttpServer server;
    late VoiceAgentService service;

    setUp(() async {
      server = await HttpServer.bind('127.0.0.1', 0);
      service = VoiceAgentService();
    });

    tearDown(() async {
      service.dispose();
      await server.close(force: true);
    });

    test('list_tools is re-sent upon reconnect after WebSocket disconnection', () async {
      final receivedTypes = <String>[];
      final listToolsCompleter = Completer<void>();
      final reconnectToolsCompleter = Completer<void>();
      int connectionCount = 0;

      server.transform(WebSocketTransformer()).listen((ws) {
        connectionCount++;
        final currentConn = connectionCount;

        ws.listen((data) {
          final obj = jsonDecode(data as String) as Map<String, dynamic>;
          final type = obj['type'] as String;
          receivedTypes.add(type);

          if (type == 'auth') {
            ws.add(jsonEncode({'type': 'auth_ok'}));
          } else if (type == 'list_tools') {
            ws.add(jsonEncode({'type': 'tools', 'tools': []}));
            if (currentConn == 1 && !listToolsCompleter.isCompleted) {
              listToolsCompleter.complete();
            } else if (currentConn == 2 && !reconnectToolsCompleter.isCompleted) {
              reconnectToolsCompleter.complete();
            }
          }
        });
      });

      // Initial connection
      await service.connect(address: '127.0.0.1:${server.port}', token: 'test_token');
      await listToolsCompleter.future.timeout(const Duration(seconds: 4));

      expect(receivedTypes, contains('list_tools'));

      // Clear received types and trigger disconnect
      receivedTypes.clear();
      service.disconnect();
      await Future.delayed(const Duration(milliseconds: 50));

      // Reconnect
      await service.connect(address: '127.0.0.1:${server.port}', token: 'test_token');
      await reconnectToolsCompleter.future.timeout(const Duration(seconds: 4));

      // Assert list_tools was sent on reconnect
      expect(receivedTypes, contains('auth'));
      expect(receivedTypes, contains('list_tools'));
    });
  });
}
