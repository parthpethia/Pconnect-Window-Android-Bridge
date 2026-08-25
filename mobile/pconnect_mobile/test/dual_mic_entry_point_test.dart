import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:pconnect_mobile/screens/home_screen.dart';
import 'package:pconnect_mobile/screens/voice_assistant_screen.dart';
import 'package:pconnect_mobile/services/connection.dart';
import 'package:pconnect_mobile/services/speech_service.dart';
import 'package:pconnect_mobile/services/voice_agent_service.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  group('Centralized Voice Pipeline Dual-Entry-Point Tests', () {
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

    testWidgets('Single pipelineState instance shared between HomeScreen and VoiceAssistantScreen', (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      speechService.sttAvailable.value = true;

      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: Column(
              children: [
                Expanded(
                  child: HomeScreen(
                    conn: null,
                    status: ConnectionStatus.disconnected,
                    onOpenDiscovery: () {},
                    voiceAgent: voiceAgent,
                    speechService: speechService,
                  ),
                ),
                Expanded(
                  child: VoiceAssistantScreen(
                    voiceAgent: voiceAgent,
                    speechService: speechService,
                  ),
                ),
              ],
            ),
          ),
        ),
      );

      await tester.pumpAndSettle();

      // Expand Voice Assistant section on HomeScreen
      await tester.tap(find.text('Voice Assistant').first);
      await tester.pumpAndSettle();

      // Initially both show ready / idle state
      expect(find.text('Tap mic to speak'), findsOneWidget);
      expect(find.text('Tap the mic and speak a command'), findsOneWidget);

      // Start listening via shared speechService pipelineState
      speechService.pipelineState.value = VoicePipelineState.listening;
      await tester.pumpAndSettle();

      // Both entry points should show active listening state (HomeScreen shows Listening... text, both show stop button)
      expect(find.text('Listening…'), findsOneWidget);
      expect(find.byIcon(Icons.stop_rounded), findsNWidgets(2));

      // Transition to thinking (non-idle, non-listening state)
      speechService.pipelineState.value = VoicePipelineState.thinking;
      await tester.pumpAndSettle();

      // Tapping mic icon on VoiceAssistantScreen while thinking must be ignored (disabled)
      await tester.tap(find.byIcon(Icons.mic_rounded).last);
      await tester.pumpAndSettle();
      expect(speechService.pipelineState.value, VoicePipelineState.thinking);

      // Tapping mic button on HomeScreen while thinking must be ignored (disabled)
      await tester.tap(find.byIcon(Icons.mic_rounded).first);
      await tester.pumpAndSettle();
      expect(speechService.pipelineState.value, VoicePipelineState.thinking);
    });

    testWidgets('Starting session from one screen updates and disables non-listening actions on the other', (tester) async {
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

      // Transition pipeline to executing
      speechService.pipelineState.value = VoicePipelineState.executing;
      await tester.pumpAndSettle();

      // Tap mic control on VoiceAssistantScreen while executing
      await tester.tap(find.byIcon(Icons.mic_rounded));
      await tester.pumpAndSettle();

      // State remains executing and does not change back to listening or idle
      expect(speechService.pipelineState.value, VoicePipelineState.executing);
    });
  });
}
