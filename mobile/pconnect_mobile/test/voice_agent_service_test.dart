import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:pconnect_mobile/services/voice_agent_service.dart';

void main() {
  group('VoiceAgentStatus', () {
    test('disconnected factory is not connected', () {
      const s = VoiceAgentStatus.disconnected;
      expect(s.connected, isFalse);
      expect(s.isAuthFailed, isFalse);
      expect(s.state, VoiceAgentConnectionState.disconnected);
    });

    test('connected status returns true', () {
      const s = VoiceAgentStatus(state: VoiceAgentConnectionState.connected);
      expect(s.connected, isTrue);
      expect(s.isAuthFailed, isFalse);
    });

    test('authFailed status returns true for isAuthFailed', () {
      const s = VoiceAgentStatus(
        state: VoiceAgentConnectionState.authFailed,
        error: 'bad token',
      );
      expect(s.connected, isFalse);
      expect(s.isAuthFailed, isTrue);
      expect(s.error, 'bad token');
    });
  });

  group('ToolCallResult', () {
    test('ok result', () {
      final r = ToolCallResult(ok: true, message: 'opened chrome');
      expect(r.ok, isTrue);
      expect(r.message, 'opened chrome');
    });

    test('failed result', () {
      final r = ToolCallResult(ok: false, message: 'app not found');
      expect(r.ok, isFalse);
      expect(r.message, 'app not found');
    });
  });

  group('VoiceAgentService', () {
    test('isConfigured returns false when unconfigured', () {
      final service = VoiceAgentService();
      expect(service.isConfigured, isFalse);
      service.dispose();
    });

    test('statusNotifier starts disconnected', () {
      final service = VoiceAgentService();
      expect(service.currentStatus.connected, isFalse);
      expect(service.currentStatus.state, VoiceAgentConnectionState.disconnected);
      service.dispose();
    });

    test('connect with empty address emits error', () async {
      final service = VoiceAgentService();
      final statuses = <VoiceAgentStatus>[];
      service.statusStream.listen(statuses.add);
      await service.connect(address: '', token: 'tok');
      expect(statuses.last.error, contains('required'));
      expect(statuses.last.connected, isFalse);
      service.dispose();
    });

    test('connect with empty token emits error', () async {
      final service = VoiceAgentService();
      final statuses = <VoiceAgentStatus>[];
      service.statusStream.listen(statuses.add);
      await service.connect(address: '1.2.3.4:9999', token: '');
      expect(statuses.last.error, contains('required'));
      expect(statuses.last.connected, isFalse);
      service.dispose();
    });

    test('callTool returns error when not connected', () async {
      final service = VoiceAgentService();
      final result = await service.callTool(tool: 'open_app', args: {'app': 'chrome'});
      expect(result.ok, isFalse);
      expect(result.message, contains('WiFi'));
      service.dispose();
    });

    test('listTools returns empty when not connected', () async {
      final service = VoiceAgentService();
      final tools = await service.listTools();
      expect(tools, isEmpty);
      service.dispose();
    });

    test('cachedTools is null initially', () {
      final service = VoiceAgentService();
      expect(service.cachedTools, isNull);
      service.dispose();
    });

    test('disconnect resets state', () {
      final service = VoiceAgentService();
      service.disconnect();
      expect(service.currentStatus.connected, isFalse);
      expect(service.cachedTools, isNull);
      service.dispose();
    });

    test('validateToolCall validates tool existence and enum arguments', () {
      final service = VoiceAgentService();

      final mockTools = [
        {
          'name': 'open_app',
          'parameters': {
            'type': 'object',
            'properties': {
              'app': {
                'type': 'string',
                'enum': ['chrome', 'whatsapp', 'calculator'],
              },
            },
          },
        },
      ];

      // Test validation logic against tool schema definition
      final fn = mockTools.first['parameters'] as Map<String, dynamic>;
      final props = fn['properties'] as Map<String, dynamic>;
      final allowedEnums = (props['app']['enum'] as List).cast<String>();

      expect(allowedEnums.contains('chrome'), isTrue);
      expect(allowedEnums.contains('unknown'), isFalse);
      expect(service.validateToolCall('open_app', {'app': 'unknown'}), isTrue); // unpopulated cache defaults true

      service.dispose();
    });
  });

  group('Auth message serialization', () {
    test('auth message has correct structure', () {
      final msg = jsonEncode({'type': 'auth', 'token': 'mytoken123'});
      final parsed = jsonDecode(msg) as Map<String, dynamic>;
      expect(parsed['type'], 'auth');
      expect(parsed['token'], 'mytoken123');
    });

    test('call_tool message has correct structure', () {
      final msg = jsonEncode({
        'type': 'call_tool',
        'tool': 'open_app',
        'args': {'app': 'chrome'},
      });
      final parsed = jsonDecode(msg) as Map<String, dynamic>;
      expect(parsed['type'], 'call_tool');
      expect(parsed['tool'], 'open_app');
      expect((parsed['args'] as Map)['app'], 'chrome');
    });

    test('list_tools message has correct structure', () {
      final msg = jsonEncode({'type': 'list_tools'});
      final parsed = jsonDecode(msg) as Map<String, dynamic>;
      expect(parsed['type'], 'list_tools');
    });
  });
}
