import 'dart:async';

import 'package:flutter/material.dart';

import '../constants/theme_tokens.dart';
import '../services/groq_service.dart';
import '../services/speech_service.dart';
import '../services/voice_agent_service.dart';
import '../widgets/glass_card.dart';

// ── Voice Command History Entry ──

class _VoiceCommandEntry {
  final String transcript;
  final String response;
  final bool success;
  final DateTime timestamp;

  _VoiceCommandEntry({
    required this.transcript,
    required this.response,
    required this.success,
    required this.timestamp,
  });
}

// ── Voice Assistant Screen ──

class VoiceAssistantScreen extends StatefulWidget {
  final VoiceAgentService voiceAgent;
  final SpeechService speechService;

  const VoiceAssistantScreen({
    super.key,
    required this.voiceAgent,
    required this.speechService,
  });

  @override
  State<VoiceAssistantScreen> createState() => _VoiceAssistantScreenState();
}

class _VoiceAssistantScreenState extends State<VoiceAssistantScreen> {
  final List<_VoiceCommandEntry> _history = [];
  bool _processing = false;
  String _statusText = 'Tap the mic and speak a command';
  StreamSubscription<String>? _transcriptSub;

  @override
  void initState() {
    super.initState();
    _transcriptSub = widget.speechService.transcriptStream.listen(_onFinalTranscript);
    widget.speechService.pipelineState.addListener(_onPipelineStateChanged);
  }

  @override
  void dispose() {
    widget.speechService.pipelineState.removeListener(_onPipelineStateChanged);
    _transcriptSub?.cancel();
    super.dispose();
  }

  void _onPipelineStateChanged() {
    if (!mounted) return;
    final state = widget.speechService.pipelineState.value;
    setState(() {
      _statusText = switch (state) {
        VoicePipelineState.idle => 'Tap the mic and speak a command',
        VoicePipelineState.listening => 'Listening…',
        VoicePipelineState.thinking => 'Thinking…',
        VoicePipelineState.executing => 'Executing command…',
        VoicePipelineState.speaking => 'Speaking response…',
      };
    });
  }

  Future<void> _onFinalTranscript(String transcript) async {
    if (transcript.isEmpty || _processing || widget.speechService.pipelineState.value != VoicePipelineState.listening) return;
    setState(() {
      _processing = true;
      _statusText = 'Processing: "$transcript"';
    });

    widget.speechService.pipelineState.value = VoicePipelineState.thinking;

    // Load Groq API key
    final apiKey = await GroqService.loadApiKey();
    if (apiKey == null || apiKey.isEmpty) {
      _finishCommand(transcript, 'Groq API key is not configured — add it in Settings', false);
      return;
    }

    // Get tools from agent
    final tools = widget.voiceAgent.cachedTools ?? [];
    if (!widget.voiceAgent.currentStatus.connected) {
      _finishCommand(transcript, "Voice agent isn't connected — check Settings", false);
      return;
    }

    // Call Groq LLM
    setState(() => _statusText = 'Thinking…');
    final groqResponse = await GroqService.chat(
      transcript: transcript,
      tools: tools,
      apiKey: apiKey,
    );

    if (groqResponse.hasError) {
      _finishCommand(transcript, groqResponse.error!, false);
      return;
    }

    if (!groqResponse.hasToolCall) {
      // Model didn't return a tool call — command not understood
      final reply = groqResponse.textReply ?? "I didn't understand that command";
      _finishCommand(transcript, reply, false);
      return;
    }

    final toolCall = groqResponse.toolCall!;

    // Client-side tool validation before dispatch
    final isValid = widget.voiceAgent.validateToolCall(toolCall.name, toolCall.arguments);
    if (!isValid) {
      final appArg = toolCall.arguments['app'];
      final msg = appArg != null ? "didn't recognize that app" : 'Tool "${toolCall.name}" is not supported';
      _finishCommand(transcript, msg, false);
      return;
    }

    // Execute tool call on PC agent
    widget.speechService.pipelineState.value = VoicePipelineState.executing;
    setState(() => _statusText = 'Executing: ${toolCall.name}…');
    final result = await widget.voiceAgent.callTool(
      tool: toolCall.name,
      args: toolCall.arguments,
    );

    _finishCommand(transcript, result.message, result.ok);
  }

  void _finishCommand(String transcript, String response, bool success) {
    if (!mounted) return;
    setState(() {
      _processing = false;
      _statusText = response;
      _history.insert(0, _VoiceCommandEntry(
        transcript: transcript,
        response: response,
        success: success,
        timestamp: DateTime.now(),
      ));
      if (_history.length > 10) _history.removeLast();
    });
    widget.speechService.speak(response);
    if (widget.speechService.pipelineState.value != VoicePipelineState.speaking) {
      widget.speechService.pipelineState.value = VoicePipelineState.idle;
    }
  }

  void _toggleListening() async {
    final state = widget.speechService.pipelineState.value;
    if (state == VoicePipelineState.listening) {
      await widget.speechService.stopListening();
    } else if (state == VoicePipelineState.idle && !_processing) {
      setState(() => _statusText = 'Listening…');
      await widget.speechService.startListening();
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(title: const Text('Voice Assistant')),
      body: Column(
        children: [
          // ── Connection status ──
          ValueListenableBuilder<VoiceAgentStatus>(
            valueListenable: widget.voiceAgent.statusNotifier,
            builder: (context, status, _) {
              final Color dotColor;
              final String label;
              switch (status.state) {
                case VoiceAgentConnectionState.connected:
                  dotColor = AppColors.success;
                  label = 'PC Agent Connected';
                case VoiceAgentConnectionState.connecting:
                case VoiceAgentConnectionState.authenticating:
                  dotColor = AppColors.warning;
                  label = 'Connecting…';
                case VoiceAgentConnectionState.authFailed:
                  dotColor = AppColors.danger;
                  label = 'Auth Failed — check Settings';
                case VoiceAgentConnectionState.disconnected:
                  dotColor = AppColors.danger;
                  label = status.error ?? 'Disconnected';
              }
              return Padding(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                child: GlassCard(
                  padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                  child: Row(
                    children: [
                      Container(
                        width: 10, height: 10,
                        decoration: BoxDecoration(shape: BoxShape.circle, color: dotColor),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Text(label, style: AppTypography.label.copyWith(color: cs.onSurface)),
                      ),
                    ],
                  ),
                ),
              );
            },
          ),

          // ── Mic button & transcript ──
          Expanded(
            child: Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  // Live transcript
                  ValueListenableBuilder<String>(
                    valueListenable: widget.speechService.liveTranscript,
                    builder: (context, transcript, _) {
                      if (transcript.isEmpty && !_processing) return const SizedBox(height: 48);
                      return Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 32, vertical: 8),
                        child: Text(
                          transcript.isNotEmpty ? '"$transcript"' : _statusText,
                          textAlign: TextAlign.center,
                          style: AppTypography.body.copyWith(
                            color: AppColors.textSecondary,
                            fontStyle: FontStyle.italic,
                          ),
                        ),
                      );
                    },
                  ),

                  const SizedBox(height: 16),

                  // Mic button
                  ValueListenableBuilder<VoicePipelineState>(
                    valueListenable: widget.speechService.pipelineState,
                    builder: (context, state, _) {
                      final listening = state == VoicePipelineState.listening;
                      final disabled = !widget.speechService.sttAvailable.value ||
                          (state != VoicePipelineState.idle && state != VoicePipelineState.listening);

                      return GestureDetector(
                        onTap: disabled ? null : _toggleListening,
                        child: AnimatedContainer(
                          duration: AppMotion.durationStandard,
                          curve: AppMotion.easeStandard,
                          width: listening ? 96 : 80,
                          height: listening ? 96 : 80,
                          decoration: BoxDecoration(
                            shape: BoxShape.circle,
                            gradient: LinearGradient(
                              colors: disabled
                                  ? [AppColors.textDisabled, AppColors.bgElevated2]
                                  : listening
                                      ? [AppColors.danger, const Color(0xFFE17055)]
                                      : [AppColors.primary, const Color(0xFF8E2DE2)],
                            ),
                            boxShadow: [
                              BoxShadow(
                                color: (listening ? AppColors.danger : AppColors.primary)
                                    .withValues(alpha: disabled ? 0.0 : 0.4),
                                blurRadius: listening ? 32 : 16,
                              ),
                            ],
                          ),
                          child: Icon(
                            listening ? Icons.stop_rounded : Icons.mic_rounded,
                            color: Colors.white,
                            size: 40,
                          ),
                        ),
                      );
                    },
                  ),

                  const SizedBox(height: 16),

                  // Status text
                  Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 32),
                    child: Text(
                      _processing ? _statusText : (widget.speechService.sttAvailable.value
                          ? 'Tap the mic and speak a command'
                          : 'Speech recognition not available on this device'),
                      textAlign: TextAlign.center,
                      style: AppTypography.caption.copyWith(color: AppColors.textDisabled),
                    ),
                  ),
                ],
              ),
            ),
          ),

          // ── Command history ──
          if (_history.isNotEmpty)
            Expanded(
              child: ListView.builder(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                itemCount: _history.length,
                itemBuilder: (context, i) {
                  final entry = _history[i];
                  return Padding(
                    padding: const EdgeInsets.only(bottom: 8),
                    child: GlassCard(
                      padding: const EdgeInsets.all(12),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              Icon(
                                entry.success ? Icons.check_circle_rounded : Icons.info_outline_rounded,
                                size: 16,
                                color: entry.success ? AppColors.success : AppColors.warning,
                              ),
                              const SizedBox(width: 8),
                              Expanded(
                                child: Text(
                                  '"${entry.transcript}"',
                                  style: AppTypography.label.copyWith(color: AppColors.textPrimary),
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                              Text(
                                _formatTime(entry.timestamp),
                                style: AppTypography.caption.copyWith(color: AppColors.textDisabled),
                              ),
                            ],
                          ),
                          const SizedBox(height: 4),
                          Text(
                            entry.response,
                            style: AppTypography.caption.copyWith(color: AppColors.textSecondary),
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ],
                      ),
                    ),
                  );
                },
              ),
            ),
        ],
      ),
    );
  }

  String _formatTime(DateTime t) {
    final h = t.hour.toString().padLeft(2, '0');
    final m = t.minute.toString().padLeft(2, '0');
    return '$h:$m';
  }
}
