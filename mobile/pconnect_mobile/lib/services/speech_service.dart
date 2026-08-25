import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter_tts/flutter_tts.dart';
import 'package:speech_to_text/speech_recognition_result.dart';
import 'package:speech_to_text/speech_to_text.dart';

enum VoicePipelineState { idle, listening, thinking, executing, speaking }

// ── Speech Service ──
// Wraps on-device STT (SpeechRecognizer / SFSpeechRecognizer)
// and TTS (TextToSpeech / AVSpeechSynthesizer).

class SpeechService {
  final SpeechToText _stt = SpeechToText();
  final FlutterTts _tts = FlutterTts();

  final ValueNotifier<bool> isListening = ValueNotifier(false);
  final ValueNotifier<String> liveTranscript = ValueNotifier('');
  final ValueNotifier<bool> isSpeaking = ValueNotifier(false);
  final ValueNotifier<VoicePipelineState> pipelineState = ValueNotifier(VoicePipelineState.idle);

  /// Whether on-device STT is available on this device.
  final ValueNotifier<bool> sttAvailable = ValueNotifier(false);

  bool _sttInitialized = false;

  final _transcriptController = StreamController<String>.broadcast();

  /// Stream of final transcripts (emitted once when the user stops speaking).
  Stream<String> get transcriptStream => _transcriptController.stream;

  SpeechService();

  // ── Initialize ──

  Future<bool> initialize() async {
    if (_sttInitialized) return sttAvailable.value;

    try {
      final available = await _stt.initialize(
        onError: (error) {
          debugPrint('SpeechService STT error: ${error.errorMsg}');
          isListening.value = false;
          sttAvailable.value = false;
        },
        onStatus: (status) {
          if (status == 'notListening' || status == 'done') {
            isListening.value = false;
          }
        },
      );
      sttAvailable.value = available;
      _sttInitialized = true;

      // Configure TTS
      await _tts.setLanguage('en-US');
      await _tts.setSpeechRate(0.5);
      await _tts.setVolume(1.0);
      _tts.setStartHandler(() {
        isSpeaking.value = true;
        pipelineState.value = VoicePipelineState.speaking;
      });
      _tts.setCompletionHandler(() {
        isSpeaking.value = false;
        pipelineState.value = VoicePipelineState.idle;
      });
      _tts.setCancelHandler(() {
        isSpeaking.value = false;
        pipelineState.value = VoicePipelineState.idle;
      });
      _tts.setErrorHandler((_) {
        isSpeaking.value = false;
        pipelineState.value = VoicePipelineState.idle;
      });

      return available;
    } catch (e) {
      debugPrint('SpeechService.initialize error: $e');
      sttAvailable.value = false;
      _sttInitialized = true;
      return false;
    }
  }

  // ── Listening ──

  Future<void> startListening() async {
    if (!sttAvailable.value || pipelineState.value != VoicePipelineState.idle) return;

    liveTranscript.value = '';
    isListening.value = true;
    pipelineState.value = VoicePipelineState.listening;

    await _stt.listen(
      onResult: _onResult,
      listenOptions: SpeechListenOptions(
        listenMode: ListenMode.dictation,
        partialResults: true,
        cancelOnError: true,
      ),
    );
  }

  void _onResult(SpeechRecognitionResult result) {
    liveTranscript.value = result.recognizedWords;

    if (result.finalResult && result.recognizedWords.isNotEmpty) {
      isListening.value = false;
      _transcriptController.add(result.recognizedWords);
    }
  }

  Future<void> stopListening() async {
    try {
      await _stt.stop();
    } catch (_) {}
    isListening.value = false;
    if (pipelineState.value == VoicePipelineState.listening) {
      pipelineState.value = VoicePipelineState.idle;
    }
  }

  // ── TTS ──

  Future<void> speak(String message) async {
    if (message.isEmpty) return;
    pipelineState.value = VoicePipelineState.speaking;
    await _tts.speak(message);
  }

  Future<void> stopSpeaking() async {
    try {
      await _tts.stop();
    } catch (_) {}
    isSpeaking.value = false;
    pipelineState.value = VoicePipelineState.idle;
  }

  // ── Dispose ──

  void dispose() {
    _transcriptController.close();
    isListening.dispose();
    liveTranscript.dispose();
    isSpeaking.dispose();
    sttAvailable.dispose();
    pipelineState.dispose();
  }
}
