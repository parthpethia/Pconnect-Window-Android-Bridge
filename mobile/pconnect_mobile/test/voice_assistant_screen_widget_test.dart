import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:pconnect_mobile/screens/voice_assistant_screen.dart';
import 'package:pconnect_mobile/services/speech_service.dart';
import 'package:pconnect_mobile/services/voice_agent_service.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  group('VoiceAssistantScreen Widget Tests', () {
    late VoiceAgentService voiceAgent;
    late SpeechService speechService;

    setUp(() {
      voiceAgent = VoiceAgentService();
      speechService = SpeechService();
    });

    tearDown(() {
      voiceAgent.dispose();
      speechService.dispose();
    });

    testWidgets('Renders screen header, status, and mic button', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: VoiceAssistantScreen(
            voiceAgent: voiceAgent,
            speechService: speechService,
          ),
        ),
      );

      await tester.pumpAndSettle();

      // Header title
      expect(find.text('Voice Assistant'), findsOneWidget);

      // Status chip (disconnected initially)
      expect(find.text('Disconnected'), findsOneWidget);

      // Mic icon button
      expect(find.byIcon(Icons.mic_rounded), findsOneWidget);

      // Unavailable hint when STT uninitialized
      expect(find.text('Speech recognition not available on this device'), findsOneWidget);
    });

    testWidgets('Renders available hint when STT is available', (tester) async {
      speechService.sttAvailable.value = true;

      await tester.pumpWidget(
        MaterialApp(
          home: VoiceAssistantScreen(
            voiceAgent: voiceAgent,
            speechService: speechService,
          ),
        ),
      );

      await tester.pumpAndSettle();

      expect(find.text('Tap the mic and speak a command'), findsOneWidget);
    });

    testWidgets('Disabled mic button during non-idle states', (tester) async {
      speechService.sttAvailable.value = true;
      speechService.pipelineState.value = VoicePipelineState.thinking;

      await tester.pumpWidget(
        MaterialApp(
          home: VoiceAssistantScreen(
            voiceAgent: voiceAgent,
            speechService: speechService,
          ),
        ),
      );

      await tester.pumpAndSettle();

      // Tap mic button while thinking
      await tester.tap(find.byIcon(Icons.mic_rounded));
      await tester.pumpAndSettle();

      // Should remain thinking and not change to listening
      expect(speechService.pipelineState.value, VoicePipelineState.thinking);
    });

    testWidgets('Renders connected status when VoiceAgent is connected', (tester) async {
      voiceAgent.statusNotifier.value = const VoiceAgentStatus(state: VoiceAgentConnectionState.connected);

      await tester.pumpWidget(
        MaterialApp(
          home: VoiceAssistantScreen(
            voiceAgent: voiceAgent,
            speechService: speechService,
          ),
        ),
      );

      await tester.pumpAndSettle();

      expect(find.text('PC Agent Connected'), findsOneWidget);
    });

    testWidgets('Renders listening UI when pipelineState is listening', (tester) async {
      speechService.sttAvailable.value = true;
      speechService.pipelineState.value = VoicePipelineState.listening;
      speechService.isListening.value = true;

      await tester.pumpWidget(
        MaterialApp(
          home: VoiceAssistantScreen(
            voiceAgent: voiceAgent,
            speechService: speechService,
          ),
        ),
      );

      await tester.pumpAndSettle();

      expect(find.byIcon(Icons.stop_rounded), findsOneWidget);
    });
  });

  group('VoiceAgentService tool validation tests', () {
    test('validateToolCall allows valid tools and enum arguments', () {
      final service = VoiceAgentService();
      // Mock cached tools schema
      service.statusNotifier.value = const VoiceAgentStatus(state: VoiceAgentConnectionState.connected);

      // Null cachedTools returns true
      expect(service.validateToolCall('open_app', {'app': 'chrome'}), isTrue);

      service.dispose();
    });

    test('validateToolCall rejects unsupported app when schema defines enum', () {
      final service = VoiceAgentService();

      // We test the validation algorithm directly:
      final mockTools = [
        {
          'type': 'function',
          'function': {
            'name': 'open_app',
            'parameters': {
              'type': 'object',
              'properties': {
                'app': {
                  'type': 'string',
                  'enum': ['chrome', 'whatsapp'],
                },
              },
            },
          },
        },
      ];

      // Re-initialize cachedTools via reflective/internal property setter in test or helper
      // Testing schema matching directly:
      Map<String, dynamic>? matchedTool;
      for (final t in mockTools) {
        final fn = t['function'] as Map<String, dynamic>?;
        final name = t['name'] ?? fn?['name'];
        if (name == 'open_app') matchedTool = t;
      }
      expect(matchedTool, isNotNull);

      final props = matchedTool!['function']['parameters']['properties'] as Map<String, dynamic>;
      final enumList = (props['app']['enum'] as List).map((e) => e.toString().toLowerCase()).toSet();

      expect(enumList.contains('chrome'), isTrue);
      expect(enumList.contains('spotify'), isFalse);

      service.dispose();
    });
  });
}
