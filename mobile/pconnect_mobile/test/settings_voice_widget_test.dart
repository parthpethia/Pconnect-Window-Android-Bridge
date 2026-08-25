import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:pconnect_mobile/main.dart';
import 'package:pconnect_mobile/screens/settings_screen.dart';
import 'package:pconnect_mobile/services/connection.dart';
import 'package:pconnect_mobile/services/speech_service.dart';
import 'package:pconnect_mobile/services/voice_agent_service.dart';
import 'package:shared_preferences/shared_preferences.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  Widget buildTestableWidget({required Widget child}) {
    return ThemeControllerScope(
      controller: AppThemeController(),
      child: MaterialApp(
        home: Scaffold(body: child),
      ),
    );
  }

  group('SettingsScreen Voice Assistant Widget Tests', () {
    late VoiceAgentService voiceAgent;

    setUp(() {
      SharedPreferences.setMockInitialValues({});
      FlutterSecureStorage.setMockInitialValues({});
      voiceAgent = VoiceAgentService();
    });

    tearDown(() {
      voiceAgent.dispose();
    });

    testWidgets('Renders Voice Assistant collapsible section in settings', (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      await tester.pumpWidget(
        buildTestableWidget(
          child: SettingsScreen(
            conn: null,
            status: ConnectionStatus.disconnected,
            onDisconnect: () {},
            voiceAgent: voiceAgent,
          ),
        ),
      );

      await tester.pumpAndSettle();

      // Verify "Voice Assistant" section header is present
      await tester.scrollUntilVisible(find.text('Voice Assistant'), 200, scrollable: find.byType(Scrollable).first);
      expect(find.text('Voice Assistant'), findsOneWidget);
    });

    testWidgets('Expands section and renders input fields and action buttons', (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      await tester.pumpWidget(
        buildTestableWidget(
          child: SettingsScreen(
            conn: null,
            status: ConnectionStatus.disconnected,
            onDisconnect: () {},
            voiceAgent: voiceAgent,
          ),
        ),
      );

      await tester.pumpAndSettle();

      // Scroll and tap to expand section
      await tester.scrollUntilVisible(find.text('Voice Assistant'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Voice Assistant'));
      await tester.pumpAndSettle();

      // Check fields exist
      expect(find.text('PC Agent Address'), findsOneWidget);
      expect(find.text('Shared Token'), findsOneWidget);
      expect(find.text('Groq API Key'), findsOneWidget);
      expect(find.text('Test'), findsOneWidget);
      expect(find.text('Save'), findsOneWidget);
      expect(find.text('Clear Voice Settings'), findsOneWidget);

      // Check security note
      expect(find.textContaining('trusted local Wi-Fi networks'), findsOneWidget);
    });

    testWidgets('Tapping Test with empty fields shows validation snackbar', (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      await tester.pumpWidget(
        buildTestableWidget(
          child: SettingsScreen(
            conn: null,
            status: ConnectionStatus.disconnected,
            onDisconnect: () {},
            voiceAgent: voiceAgent,
          ),
        ),
      );

      await tester.pumpAndSettle();

      // Scroll & Expand
      await tester.scrollUntilVisible(find.text('Voice Assistant'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Voice Assistant'));
      await tester.pumpAndSettle();

      // Tap Test button
      await tester.tap(find.widgetWithText(OutlinedButton, 'Test'));
      await tester.pumpAndSettle();

      // Verify validation message
      expect(find.text('Enter both address and token first'), findsOneWidget);
    });

    testWidgets('Tapping Clear Voice Settings shows confirm dialog', (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      await tester.pumpWidget(
        buildTestableWidget(
          child: SettingsScreen(
            conn: null,
            status: ConnectionStatus.disconnected,
            onDisconnect: () {},
            voiceAgent: voiceAgent,
          ),
        ),
      );

      await tester.pumpAndSettle();

      // Scroll & Expand
      await tester.scrollUntilVisible(find.text('Voice Assistant'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Voice Assistant'));
      await tester.pumpAndSettle();

      // Scroll & Tap Clear Voice Settings
      await tester.scrollUntilVisible(find.text('Clear Voice Settings'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Clear Voice Settings'));
      await tester.pumpAndSettle();

      // Confirm dialog title & content
      expect(find.text('Clear Voice Settings'), findsNWidgets(2)); // section header + dialog title
      expect(find.text('This will remove the saved PC agent address, shared token, and Groq API key. Continue?'), findsOneWidget);

      // Tap Clear All
      await tester.tap(find.text('Clear All'));
      await tester.pumpAndSettle();

      // SnackBar confirmation
      expect(find.text('Voice settings cleared'), findsOneWidget);
    });

    testWidgets('Tapping Clear Voice Settings resets active SpeechService state', (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      final speechService = SpeechService();
      speechService.pipelineState.value = VoicePipelineState.listening;
      speechService.liveTranscript.value = 'active transcript';

      await tester.pumpWidget(
        buildTestableWidget(
          child: SettingsScreen(
            conn: null,
            status: ConnectionStatus.disconnected,
            onDisconnect: () {},
            voiceAgent: voiceAgent,
            speechService: speechService,
          ),
        ),
      );

      await tester.pumpAndSettle();

      await tester.scrollUntilVisible(find.text('Voice Assistant'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Voice Assistant'));
      await tester.pumpAndSettle();

      await tester.scrollUntilVisible(find.text('Clear Voice Settings'), 200, scrollable: find.byType(Scrollable).first);
      await tester.tap(find.text('Clear Voice Settings'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Clear All'));
      await tester.pumpAndSettle();

      expect(speechService.pipelineState.value, VoicePipelineState.idle);
      expect(speechService.liveTranscript.value, isEmpty);

      speechService.dispose();
    });
  });
}
