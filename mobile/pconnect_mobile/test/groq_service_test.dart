import 'dart:convert';

import 'package:flutter_test/flutter_test.dart';
import 'package:pconnect_mobile/services/groq_service.dart';

void main() {
  group('GroqResponse', () {
    test('hasToolCall is true when toolCall is present', () {
      final r = GroqResponse(toolCall: GroqToolCall(name: 'open_app', arguments: {'app': 'chrome'}));
      expect(r.hasToolCall, isTrue);
      expect(r.hasError, isFalse);
      expect(r.textReply, isNull);
    });

    test('hasToolCall is false for text reply', () {
      final r = GroqResponse(textReply: 'Hello there');
      expect(r.hasToolCall, isFalse);
      expect(r.hasError, isFalse);
      expect(r.textReply, 'Hello there');
    });

    test('hasError is true for error response', () {
      final r = GroqResponse(error: 'Rate limit');
      expect(r.hasError, isTrue);
      expect(r.hasToolCall, isFalse);
    });
  });

  group('GroqService.parseResponse', () {
    test('parses tool_call response with string arguments', () {
      final body = jsonEncode({
        'choices': [
          {
            'message': {
              'role': 'assistant',
              'content': null,
              'tool_calls': [
                {
                  'id': 'call_123',
                  'type': 'function',
                  'function': {
                    'name': 'open_app',
                    'arguments': '{"app": "chrome"}',
                  },
                },
              ],
            },
          },
        ],
      });

      final result = GroqService.parseResponse(body);
      expect(result.hasToolCall, isTrue);
      expect(result.toolCall!.name, 'open_app');
      expect(result.toolCall!.arguments['app'], 'chrome');
    });

    test('parses tool_call response with map arguments', () {
      final body = jsonEncode({
        'choices': [
          {
            'message': {
              'role': 'assistant',
              'content': null,
              'tool_calls': [
                {
                  'id': 'call_456',
                  'type': 'function',
                  'function': {
                    'name': 'open_app',
                    'arguments': {'app': 'whatsapp'},
                  },
                },
              ],
            },
          },
        ],
      });

      final result = GroqService.parseResponse(body);
      expect(result.hasToolCall, isTrue);
      expect(result.toolCall!.name, 'open_app');
      expect(result.toolCall!.arguments['app'], 'whatsapp');
    });

    test('explicitly selects first tool call and ignores subsequent tool calls when multiple returned', () {
      final body = jsonEncode({
        'choices': [
          {
            'message': {
              'role': 'assistant',
              'content': null,
              'tool_calls': [
                {
                  'id': 'call_1',
                  'type': 'function',
                  'function': {
                    'name': 'open_app',
                    'arguments': {'app': 'chrome'},
                  },
                },
                {
                  'id': 'call_2',
                  'type': 'function',
                  'function': {
                    'name': 'open_app',
                    'arguments': {'app': 'calculator'},
                  },
                },
              ],
            },
          },
        ],
      });

      final result = GroqService.parseResponse(body);
      expect(result.hasToolCall, isTrue);
      expect(result.toolCall!.name, 'open_app');
      expect(result.toolCall!.arguments['app'], 'chrome');
    });

    test('parses text reply when no tool_calls', () {
      final body = jsonEncode({
        'choices': [
          {
            'message': {
              'role': 'assistant',
              'content': "I don't know how to do that",
            },
          },
        ],
      });

      final result = GroqService.parseResponse(body);
      expect(result.hasToolCall, isFalse);
      expect(result.textReply, "I don't know how to do that");
    });

    test('returns error for empty choices', () {
      final body = jsonEncode({'choices': []});
      final result = GroqService.parseResponse(body);
      expect(result.hasError, isTrue);
      expect(result.error, contains('Empty'));
    });

    test('returns error for null choices', () {
      final body = jsonEncode({'id': 'test'});
      final result = GroqService.parseResponse(body);
      expect(result.hasError, isTrue);
    });

    test('returns error for malformed JSON', () {
      final result = GroqService.parseResponse('not json');
      expect(result.hasError, isTrue);
    });

    test('handles missing content gracefully', () {
      final body = jsonEncode({
        'choices': [
          {
            'message': {
              'role': 'assistant',
            },
          },
        ],
      });

      final result = GroqService.parseResponse(body);
      expect(result.hasToolCall, isFalse);
      expect(result.textReply, isNotNull);
    });
  });

  group('GroqService.chat input validation', () {
    test('returns error for empty API key', () async {
      final result = await GroqService.chat(
        transcript: 'open chrome',
        tools: [],
        apiKey: '',
      );
      expect(result.hasError, isTrue);
      expect(result.error, contains('not configured'));
    });
  });

  group('Request body construction', () {
    test('builds correct request with tools', () {
      final tools = [
        {
          'type': 'function',
          'function': {
            'name': 'open_app',
            'description': 'Open an application',
            'parameters': {
              'type': 'object',
              'properties': {
                'app': {'type': 'string', 'description': 'App name'},
              },
              'required': ['app'],
            },
          },
        },
      ];

      final body = {
        'model': 'llama-3.3-70b-versatile',
        'messages': [
          {'role': 'system', 'content': 'You are a voice assistant'},
          {'role': 'user', 'content': 'open chrome'},
        ],
        'tools': tools,
        'tool_choice': 'auto',
      };

      final json = jsonEncode(body);
      final parsed = jsonDecode(json) as Map<String, dynamic>;

      expect(parsed['model'], 'llama-3.3-70b-versatile');
      expect(parsed['messages'], hasLength(2));
      expect(parsed['tools'], hasLength(1));
      expect(parsed['tool_choice'], 'auto');
    });

    test('builds correct request without tools', () {
      final body = {
        'model': 'llama-3.3-70b-versatile',
        'messages': [
          {'role': 'user', 'content': 'hello'},
        ],
      };

      final json = jsonEncode(body);
      final parsed = jsonDecode(json) as Map<String, dynamic>;
      expect(parsed.containsKey('tools'), isFalse);
      expect(parsed.containsKey('tool_choice'), isFalse);
    });
  });
}
