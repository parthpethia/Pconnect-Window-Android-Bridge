import 'dart:convert';

import 'package:flutter/foundation.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;

// ── Groq API Constants ──

const String _kGroqEndpoint = 'https://api.groq.com/openai/v1/chat/completions';
const String _kGroqModel = 'llama-3.3-70b-versatile';
const String _kGroqApiKeyStorageKey = 'groq_api_key';

// ── Parsed Response ──

class GroqToolCall {
  final String name;
  final Map<String, dynamic> arguments;
  GroqToolCall({required this.name, required this.arguments});
}

class GroqResponse {
  /// Non-null when the model returned a tool/function call.
  final GroqToolCall? toolCall;

  /// Text reply when the model did NOT issue a tool call.
  final String? textReply;

  /// Error message when the API call failed entirely.
  final String? error;

  GroqResponse({this.toolCall, this.textReply, this.error});

  bool get hasToolCall => toolCall != null;
  bool get hasError => error != null;
}

// ── Groq Service ──

class GroqService {
  static const _storage = FlutterSecureStorage();

  /// Load the Groq API key from secure storage.
  static Future<String?> loadApiKey() async {
    try {
      return await _storage.read(key: _kGroqApiKeyStorageKey);
    } catch (_) {
      return null;
    }
  }

  /// Save the Groq API key to secure storage.
  static Future<void> saveApiKey(String key) async {
    await _storage.write(key: _kGroqApiKeyStorageKey, value: key);
  }

  /// Delete the Groq API key from secure storage.
  static Future<void> deleteApiKey() async {
    await _storage.delete(key: _kGroqApiKeyStorageKey);
  }

  /// Send a user transcript to Groq with tool definitions and return the
  /// model's response (either a tool call or a text reply).
  ///
  /// [transcript] — The user's spoken command text.
  /// [tools] — OpenAI/Groq-style function-calling schemas from the agent's `list_tools`.
  /// [apiKey] — Groq API key (loaded from secure storage by caller).
  static Future<GroqResponse> chat({
    required String transcript,
    required List<Map<String, dynamic>> tools,
    required String apiKey,
  }) async {
    if (apiKey.isEmpty) {
      return GroqResponse(error: 'Groq API key is not configured — add it in Settings');
    }

    final body = <String, dynamic>{
      'model': _kGroqModel,
      'messages': [
        {
          'role': 'system',
          'content': 'You are a voice assistant that controls a PC over LAN. '
              'When the user asks to open an app or run an action, call the appropriate tool. '
              'If you cannot match the request to a tool, reply with a short helpful message.',
        },
        {
          'role': 'user',
          'content': transcript,
        },
      ],
      if (tools.isNotEmpty) 'tools': tools,
      if (tools.isNotEmpty) 'tool_choice': 'auto',
    };

    try {
      final response = await http.post(
        Uri.parse(_kGroqEndpoint),
        headers: {
          'Authorization': 'Bearer $apiKey',
          'Content-Type': 'application/json',
        },
        body: jsonEncode(body),
      ).timeout(const Duration(seconds: 10));

      if (response.statusCode == 401) {
        return GroqResponse(error: 'Invalid Groq API key — check Settings');
      }
      if (response.statusCode == 429) {
        return GroqResponse(error: 'Groq rate limit reached — try again in a moment');
      }
      if (response.statusCode != 200) {
        return GroqResponse(error: 'Groq API error (${response.statusCode})');
      }

      return parseResponse(response.body);
    } catch (e) {
      debugPrint('GroqService.chat error: $e');
      return GroqResponse(error: 'Could not reach Groq — check your internet connection');
    }
  }

  /// Parse a Groq API response body into a [GroqResponse].
  /// Exposed as a static method for testability.
  static GroqResponse parseResponse(String responseBody) {
    try {
      final json = jsonDecode(responseBody) as Map<String, dynamic>;
      final choices = json['choices'] as List<dynamic>?;
      if (choices == null || choices.isEmpty) {
        return GroqResponse(error: 'Empty response from Groq');
      }

      final message = choices[0]['message'] as Map<String, dynamic>;
      final toolCalls = message['tool_calls'] as List<dynamic>?;

      if (toolCalls != null && toolCalls.isNotEmpty) {
        // If the model returned multiple tool calls, explicitly select the first tool call
        // and ignore any subsequent ones to enforce single-command predictability and avoid side effects.
        final first = toolCalls[0] as Map<String, dynamic>;
        final function_ = first['function'] as Map<String, dynamic>;
        final name = function_['name'] as String;
        final argsRaw = function_['arguments'];
        final args = argsRaw is String
            ? (jsonDecode(argsRaw) as Map<String, dynamic>)
            : (argsRaw as Map<String, dynamic>? ?? {});

        return GroqResponse(toolCall: GroqToolCall(name: name, arguments: args));
      }

      // No tool call — return text reply
      final content = message['content'] as String? ?? "I didn't understand that command";
      return GroqResponse(textReply: content);
    } catch (e) {
      debugPrint('GroqService.parseResponse error: $e');
      return GroqResponse(error: 'Failed to parse Groq response');
    }
  }
}
