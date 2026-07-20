import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';
import '../services/connection.dart';
import '../widgets/screen_preview_webrtc.dart';
import 'diagnostics_screen.dart';

/// Screen capture quality presets — balances sharpness vs bandwidth.
enum ScreenQuality {
  normal(label: 'Normal', width: 1080, quality: 75, intervalMs: 900),
  high(label: 'High', width: 1440, quality: 80, intervalMs: 800),
  best(label: 'Best', width: 1920, quality: 90, intervalMs: 600);

  final String label;
  final int width;
  final int quality;
  final int intervalMs;
  const ScreenQuality({required this.label, required this.width, required this.quality, required this.intervalMs});

  static ScreenQuality fromName(String? name) {
    switch (name) {
      case 'high': return ScreenQuality.high;
      case 'best': return ScreenQuality.best;
      default: return ScreenQuality.normal;
    }
  }
}

/// A dedicated remote-control page:
///  • Top half  – live PC screen preview
///  • Bottom half – toggle between Trackpad / Keyboard
///  • Fullscreen button at the bottom to go immersive
class RemoteControlScreen extends StatefulWidget {
  final PcConnection? conn;
  final bool connected;

  const RemoteControlScreen({
    super.key,
    required this.conn,
    required this.connected,
  });

  @override
  State<RemoteControlScreen> createState() => _RemoteControlScreenState();
}

class _RemoteControlScreenState extends State<RemoteControlScreen> {
  bool _screenOn = false;
  int _modeIndex = 0; // 0 = trackpad, 1 = keyboard
  ScreenQuality _quality = ScreenQuality.normal;

  @override
  void initState() {
    super.initState();
    _loadQualityPref();
  }

  Future<void> _loadQualityPref() async {
    final prefs = await SharedPreferences.getInstance();
    if (mounted) {
      setState(() {
        _quality = ScreenQuality.fromName(prefs.getString('screen_quality'));
      });
    }
  }

  Future<void> _setQuality(ScreenQuality q) async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.setString('screen_quality', q.name);
    setState(() => _quality = q);
    // Restart capture with new quality if preview is on
    if (_screenOn) {
      widget.conn?.stopScreenCapture();
      widget.conn?.startScreenCapture(
        intervalMs: q.intervalMs,
        width: q.width,
        quality: q.quality,
      );
    }
  }

  @override
  void dispose() {
    if (_screenOn) widget.conn?.stopScreenCapture();
    super.dispose();
  }

  void _togglePreview(bool v) {
    setState(() => _screenOn = v);
    if (v) {
      widget.conn?.startScreenCapture(
        intervalMs: _quality.intervalMs,
        width: _quality.width,
        quality: _quality.quality,
      );
    } else {
      widget.conn?.stopScreenCapture();
    }
  }

  void _openFullscreen() {
    if (!widget.connected) return;
    // Ensure preview is on
    if (!_screenOn) _togglePreview(true);
    Navigator.of(context).push(MaterialPageRoute(
      builder: (_) => _FullscreenRemote(
        conn: widget.conn,
        initialMode: _modeIndex,
        quality: _quality,
      ),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final conn = widget.conn;
    if (conn == null) {
      return Scaffold(
        appBar: AppBar(title: const Text('Remote Control')),
        body: const Center(child: Text('No PC connection')),
      );
    }

    return ValueListenableBuilder<ConnectionStatus>(
      valueListenable: conn.statusNotifier,
      builder: (context, status, _) {
        final cs = Theme.of(context).colorScheme;
        final enabled = status.connected;
        final hasError = status.error != null;

        return Scaffold(
          appBar: AppBar(
            title: const Text('Remote Control'),
            actions: [
              if (hasError || !enabled)
                IconButton(
                  icon: const Icon(Icons.error_outline_rounded, color: Colors.redAccent),
                  tooltip: 'Connection Error',
                  onPressed: () => _showErrorDialog(context, conn, status),
                ),
              // Quality selector
              PopupMenuButton<ScreenQuality>(
                icon: Icon(
                  _quality == ScreenQuality.best
                      ? Icons.hd_rounded
                      : _quality == ScreenQuality.high
                          ? Icons.high_quality_rounded
                          : Icons.sd_rounded,
                  size: 20,
                  color: cs.primary,
                ),
                tooltip: 'Preview Quality',
                onSelected: enabled ? _setQuality : null,
                itemBuilder: (_) => ScreenQuality.values.map((q) {
                  final active = q == _quality;
                  return PopupMenuItem(
                    value: q,
                    child: Row(
                      children: [
                        Icon(
                          active ? Icons.radio_button_checked : Icons.radio_button_off,
                          size: 18,
                          color: active ? cs.primary : cs.onSurface.withValues(alpha: 0.5),
                        ),
                        const SizedBox(width: 10),
                        Text(
                          q.label,
                          style: TextStyle(
                            fontWeight: active ? FontWeight.w600 : FontWeight.normal,
                            color: active ? cs.primary : cs.onSurface,
                          ),
                        ),
                        const SizedBox(width: 8),
                        Text(
                          '${q.width}p',
                          style: TextStyle(fontSize: 11, color: cs.onSurface.withValues(alpha: 0.4)),
                        ),
                      ],
                    ),
                  );
                }).toList(),
              ),
              // Preview toggle
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text('Preview', style: TextStyle(fontSize: 12, color: cs.onSurface)),
                  Switch(
                    value: _screenOn,
                    onChanged: enabled ? _togglePreview : null,
                  ),
                ],
              ),
            ],
          ),
          body: Column(
            children: [
              // ── TOP: Screen Preview ──
              Expanded(
                flex: 5,
                child: _PreviewPanel(
                  conn: conn,
                  screenOn: _screenOn && enabled,
                  cs: cs,
                  quality: _quality,
                ),
              ),

              // ── Mode toggle chips ──
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 6),
                child: Row(
                  children: [
                    _ModeChip(
                      icon: Icons.touch_app_rounded,
                      label: 'Trackpad',
                      selected: _modeIndex == 0,
                      onTap: () => setState(() => _modeIndex = 0),
                      cs: cs,
                    ),
                    const SizedBox(width: 8),
                    _ModeChip(
                      icon: Icons.keyboard_rounded,
                      label: 'Keyboard',
                      selected: _modeIndex == 1,
                      onTap: () => setState(() => _modeIndex = 1),
                      cs: cs,
                    ),
                  ],
                ),
              ),

              // ── BOTTOM: Trackpad or Keyboard ──
              Expanded(
                flex: 5,
                child: Stack(
                  children: [
                    AnimatedSwitcher(
                      duration: const Duration(milliseconds: 250),
                      child: _modeIndex == 0
                          ? _EmbeddedTrackpad(key: const ValueKey('tp'), conn: conn, enabled: enabled)
                          : _EmbeddedKeyboard(key: const ValueKey('kb'), conn: conn, enabled: enabled),
                    ),
                    if (!enabled)
                      Positioned.fill(
                        child: Container(
                          margin: const EdgeInsets.symmetric(horizontal: 16),
                          decoration: BoxDecoration(
                            color: Colors.black.withValues(alpha: 0.65),
                            borderRadius: BorderRadius.circular(16),
                          ),
                          child: const Center(
                            child: Column(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                Icon(Icons.wifi_off_rounded, color: Colors.white54, size: 40),
                                SizedBox(height: 8),
                                Text(
                                  'Disconnected',
                                  style: TextStyle(
                                    color: Colors.white70,
                                    fontSize: 14,
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ),
                      ),
                  ],
                ),
              ),

              // ── Fullscreen button ──
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
                child: SizedBox(
                  width: double.infinity,
                  height: 48,
                  child: FilledButton.icon(
                    onPressed: enabled ? _openFullscreen : null,
                    icon: const Icon(Icons.fullscreen_rounded, size: 24),
                    label: const Text('Fullscreen', style: TextStyle(fontSize: 15, fontWeight: FontWeight.w600)),
                    style: FilledButton.styleFrom(
                      shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(14)),
                    ),
                  ),
                ),
              ),
            ],
          ),
        );
      },
    );
  }
}

// ─────────────────────────────────────────
// Mode toggle chip
// ─────────────────────────────────────────
class _ModeChip extends StatelessWidget {
  final IconData icon;
  final String label;
  final bool selected;
  final VoidCallback onTap;
  final ColorScheme cs;

  const _ModeChip({
    required this.icon,
    required this.label,
    required this.selected,
    required this.onTap,
    required this.cs,
  });

  @override
  Widget build(BuildContext context) {
    return Expanded(
      child: GestureDetector(
        onTap: onTap,
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 200),
          padding: const EdgeInsets.symmetric(vertical: 8),
          decoration: BoxDecoration(
            color: selected ? cs.primaryContainer : cs.surfaceContainerHighest.withValues(alpha: 0.5),
            borderRadius: BorderRadius.circular(12),
            border: selected ? Border.all(color: cs.primary, width: 1.5) : null,
          ),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Icon(icon, size: 18, color: selected ? cs.primary : cs.onSurface.withValues(alpha: 0.5)),
              const SizedBox(width: 6),
              Text(
                label,
                style: TextStyle(
                  fontSize: 13,
                  fontWeight: selected ? FontWeight.w600 : FontWeight.normal,
                  color: selected ? cs.primary : cs.onSurface.withValues(alpha: 0.6),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

// ─────────────────────────────────────────
// Preview panel
// ─────────────────────────────────────────
class _PreviewPanel extends StatelessWidget {
  final PcConnection? conn;
  final bool screenOn;
  final ColorScheme cs;
  final ScreenQuality quality;

  const _PreviewPanel({required this.conn, required this.screenOn, required this.cs, required this.quality});

  @override
  Widget build(BuildContext context) {
    if (!screenOn || conn == null) {
      return Container(
        margin: const EdgeInsets.fromLTRB(16, 8, 16, 4),
        decoration: BoxDecoration(
          color: cs.surfaceContainerHighest,
          borderRadius: BorderRadius.circular(16),
        ),
        child: Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(Icons.desktop_windows_rounded, size: 48, color: cs.onSurface.withValues(alpha: 0.2)),
              const SizedBox(height: 8),
              Text('Turn on Preview', style: TextStyle(color: cs.onSurface.withValues(alpha: 0.3))),
            ],
          ),
        ),
      );
    }

    return Container(
      margin: const EdgeInsets.fromLTRB(16, 8, 16, 4),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: cs.outlineVariant.withValues(alpha: 0.3)),
      ),
      clipBehavior: Clip.antiAlias,
      child: InteractiveViewer(
        minScale: 1.0,
        maxScale: 6.0,
        child: ValueListenableBuilder<RTCVideoRenderer?>(
          valueListenable: conn!.webrtcRendererNotifier,
          builder: (context, renderer, _) {
            if (renderer != null) {
              return ScreenPreviewWebRtc(renderer: renderer);
            }
            return ValueListenableBuilder<Uint8List?>(
              valueListenable: conn!.screenFrameNotifier,
              builder: (context, frame, _) {
                if (frame == null) {
                  return Center(child: CircularProgressIndicator(color: cs.primary));
                }
                return Image.memory(
                  frame,
                  gaplessPlayback: true,
                  fit: BoxFit.contain,
                  filterQuality: quality == ScreenQuality.best
                      ? FilterQuality.high
                      : FilterQuality.medium,
                  errorBuilder: (context, error, stackTrace) {
                    return Container(
                      color: Colors.grey.shade900,
                      child: const Center(
                        child: Icon(Icons.broken_image_rounded, color: Colors.white30, size: 36),
                      ),
                    );
                  },
                );
              },
            );
          },
        ),
      ),
    );
  }
}

// ─────────────────────────────────────────
// Embedded Trackpad
// ─────────────────────────────────────────
class _EmbeddedTrackpad extends StatefulWidget {
  final PcConnection? conn;
  final bool enabled;
  const _EmbeddedTrackpad({super.key, required this.conn, required this.enabled});
  @override
  State<_EmbeddedTrackpad> createState() => _EmbeddedTrackpadState();
}

class _EmbeddedTrackpadState extends State<_EmbeddedTrackpad> {
  double _sensitivity = 1.4;
  bool _invertScroll = false;

  final Map<int, Offset> _pointers = {};
  Offset? _lastCentroid;
  double _accumDx = 0, _accumDy = 0, _accumWheel = 0;
  Timer? _flush;
  Timer? _longPress;
  bool _dragging = false;
  int? _dragPointer;
  Offset? _downPos;
  DateTime? _downTime;
  int _downPointerCount = 0;

  @override
  void initState() {
    super.initState();
    _loadPrefs();
  }

  Future<void> _loadPrefs() async {
    final prefs = await SharedPreferences.getInstance();
    if (mounted) {
      setState(() {
        _sensitivity = prefs.getDouble('trackpad_sensitivity') ?? 1.4;
        _invertScroll = prefs.getBool('invert_scroll') ?? false;
      });
    }
  }

  @override
  void dispose() {
    _flush?.cancel();
    _longPress?.cancel();
    if (_dragging) widget.conn?.mouseButton(button: 'left', action: 'up');
    super.dispose();
  }

  void _startFlush() {
    _flush ??= Timer.periodic(const Duration(milliseconds: 16), (_) {
      final dx = _accumDx.truncate(), dy = _accumDy.truncate(), w = _accumWheel.truncate();
      _accumDx -= dx; _accumDy -= dy; _accumWheel -= w;
      if (dx != 0 || dy != 0) widget.conn?.mouseMove(dx: dx, dy: dy);
      if (w != 0) widget.conn?.mouseScroll(dy: w);
      if (_pointers.isEmpty) { _flush?.cancel(); _flush = null; }
    });
  }

  Offset _centroid() {
    if (_pointers.isEmpty) return Offset.zero;
    var sum = Offset.zero;
    for (final p in _pointers.values) {
      sum += p;
    }
    return sum / _pointers.length.toDouble();
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final enabled = widget.enabled;

    return Column(
      children: [
        Expanded(
          child: Listener(
            onPointerDown: !enabled ? null : (e) {
              _pointers[e.pointer] = e.localPosition;
              _downPos = e.localPosition;
              _downTime = DateTime.now();
              _downPointerCount = _pointers.length;
              if (_pointers.length == 1) {
                _longPress?.cancel();
                _longPress = Timer(const Duration(milliseconds: 350), () {
                  if (_pointers.length != 1) return;
                  final moved = (_pointers.values.first - _downPos!).distance;
                  if (moved > 8) return;
                  _dragging = true;
                  _dragPointer = e.pointer;
                  widget.conn?.mouseButton(button: 'left', action: 'down');
                });
              } else {
                _longPress?.cancel();
                _lastCentroid = _centroid();
              }
              _startFlush();
            },
            onPointerMove: !enabled ? null : (e) {
              final prev = _pointers[e.pointer];
              if (prev == null) return;
              _pointers[e.pointer] = e.localPosition;
              if (_pointers.length == 1) {
                final d = e.localPosition - prev;
                _accumDx += d.dx * _sensitivity;
                _accumDy += d.dy * _sensitivity;
              } else {
                final c = _centroid();
                if (_lastCentroid != null) {
                  final d = c - _lastCentroid!;
                  final scrollDir = _invertScroll ? 1.0 : -1.0;
                  _accumWheel += d.dy * scrollDir * 2.0;
                }
                _lastCentroid = c;
              }
            },
            onPointerUp: !enabled ? null : (e) {
              final pos = _pointers.remove(e.pointer);
              _longPress?.cancel();
              if (_dragging && _dragPointer == e.pointer) {
                widget.conn?.mouseButton(button: 'left', action: 'up');
                _dragging = false;
                _dragPointer = null;
                return;
              }
              if (pos != null && _pointers.isEmpty && _downTime != null) {
                final dt = DateTime.now().difference(_downTime!).inMilliseconds;
                final dist = (pos - _downPos!).distance;
                if (dt <= 220 && dist <= 10) {
                  if (_downPointerCount >= 2) {
                    widget.conn?.mouseButton(button: 'right', action: 'click');
                  } else {
                    widget.conn?.mouseButton(button: 'left', action: 'click');
                  }
                }
              }
              _lastCentroid = _pointers.length >= 2 ? _centroid() : null;
              _downPointerCount = _pointers.length;
            },
            onPointerCancel: !enabled ? null : (e) {
              _pointers.remove(e.pointer);
              _longPress?.cancel();
              if (_dragging && _dragPointer == e.pointer) {
                widget.conn?.mouseButton(button: 'left', action: 'up');
                _dragging = false;
              }
            },
            child: Container(
              margin: const EdgeInsets.symmetric(horizontal: 16),
              decoration: BoxDecoration(
                color: cs.surfaceContainerHighest,
                borderRadius: BorderRadius.circular(16),
                border: Border.all(color: cs.outlineVariant.withValues(alpha: 0.4)),
              ),
              child: Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const Icon(Icons.touch_app, size: 32, color: Colors.white24),
                    const SizedBox(height: 4),
                    const Text(
                      'Tap · 2-finger right click\nLong press drag · 2-finger scroll',
                      textAlign: TextAlign.center,
                      style: TextStyle(fontSize: 10, color: Colors.white24),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
        // Mouse buttons
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 6, 16, 4),
          child: Row(
            children: [
              Expanded(
                child: SizedBox(
                  height: 36,
                  child: FilledButton.tonal(
                    onPressed: enabled ? () => widget.conn?.mouseButton(button: 'left', action: 'click') : null,
                    style: FilledButton.styleFrom(padding: EdgeInsets.zero),
                    child: const Text('L', style: TextStyle(fontSize: 13)),
                  ),
                ),
              ),
              const SizedBox(width: 6),
              Expanded(
                child: SizedBox(
                  height: 36,
                  child: FilledButton.tonal(
                    onPressed: enabled ? () => widget.conn?.mouseButton(button: 'middle', action: 'click') : null,
                    style: FilledButton.styleFrom(padding: EdgeInsets.zero),
                    child: const Text('M', style: TextStyle(fontSize: 13)),
                  ),
                ),
              ),
              const SizedBox(width: 6),
              Expanded(
                child: SizedBox(
                  height: 36,
                  child: FilledButton.tonal(
                    onPressed: enabled ? () => widget.conn?.mouseButton(button: 'right', action: 'click') : null,
                    style: FilledButton.styleFrom(padding: EdgeInsets.zero),
                    child: const Text('R', style: TextStyle(fontSize: 13)),
                  ),
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

// ─────────────────────────────────────────
// Embedded Keyboard
// ─────────────────────────────────────────
class _EmbeddedKeyboard extends StatefulWidget {
  final PcConnection? conn;
  final bool enabled;
  const _EmbeddedKeyboard({super.key, required this.conn, required this.enabled});
  @override
  State<_EmbeddedKeyboard> createState() => _EmbeddedKeyboardState();
}

class _EmbeddedKeyboardState extends State<_EmbeddedKeyboard> {
  final _tc = TextEditingController();
  final _focusNode = FocusNode();
  Timer? _debounce;

  @override
  void initState() {
    super.initState();
    widget.conn?.resetKeyboardText();
    _tc.addListener(_onTextRaw);
    _focusNode.addListener(_onFocusChanged);
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _tc.dispose();
    _focusNode.dispose();
    super.dispose();
  }

  void _onFocusChanged() {
    if (_focusNode.hasFocus) {
      widget.conn?.resetKeyboardText();
      _tc.text = '';
    }
  }

  /// Debounce rapid text mutations (autocorrect, predictive text) to avoid
  /// sending spurious backspaces. Fires 50ms after the last change.
  void _onTextRaw() {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 50), _flushText);
  }

  void _flushText() {
    if (!widget.enabled || widget.conn == null) return;
    if (_tc.value.composing.isValid) return;

    final current = _tc.text;
    final conn = widget.conn!;
    final diff = TextDiff.compute(conn.lastKeyboardText, current);
    
    if (diff.backspaces == 0 && diff.inserted.isEmpty) {
      conn.resetKeyboardText(value: current);
      return;
    }

    if (conn.isReplaceAll(diff, conn.lastKeyboardText)) {
      conn.sendReplaceAllText(text: current);
    } else {
      conn.sendInput(backspaces: diff.backspaces, text: diff.inserted);
    }
    conn.resetKeyboardText(value: current);
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final isLandscape = MediaQuery.of(context).orientation == Orientation.landscape;

    final shortcutWidget = Wrap(
      spacing: 6,
      runSpacing: 6,
      children: [
        _chip('Ctrl+C', ['ctrl', 'c']),
        _chip('Ctrl+V', ['ctrl', 'v']),
        _chip('Ctrl+Z', ['ctrl', 'z']),
        _chip('Ctrl+A', ['ctrl', 'a']),
        _chip('Alt+Tab', ['alt', 'tab']),
        _chip('Alt+F4', ['alt', 'f4']),
        _chip('Win+D', ['win', 'd']),
        _chip('Enter', ['enter']),
        _chip('Esc', ['esc']),
        _chip('Tab', ['tab']),
        _chip('Del', ['delete']),
      ],
    );

    final arrowKeysWidget = Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        _arrowBtn(Icons.arrow_back, ['left']),
        Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            _arrowBtn(Icons.arrow_upward, ['up']),
            _arrowBtn(Icons.arrow_downward, ['down']),
          ],
        ),
        _arrowBtn(Icons.arrow_forward, ['right']),
      ],
    );

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16),
      child: Column(
        children: [
          TextField(
            controller: _tc,
            focusNode: _focusNode,
            maxLines: isLandscape ? 1 : 2,
            enabled: widget.enabled,
            autocorrect: false,
            enableSuggestions: false,
            decoration: InputDecoration(
              labelText: 'Type here → PC',
              border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
              filled: true,
              fillColor: cs.surfaceContainerHighest.withValues(alpha: 0.5),
            ),
          ),
          const SizedBox(height: 8),
          Expanded(
            child: SingleChildScrollView(
              child: Column(
                children: [
                  shortcutWidget,
                  if (isLandscape) ...[
                    const SizedBox(height: 12),
                    arrowKeysWidget,
                  ],
                ],
              ),
            ),
          ),
          if (!isLandscape) ...[
            const SizedBox(height: 8),
            Padding(
              padding: const EdgeInsets.only(bottom: 4),
              child: arrowKeysWidget,
            ),
          ],
        ],
      ),
    );
  }

  Widget _chip(String label, List<String> keys) {
    return ActionChip(
      label: Text(label, style: const TextStyle(fontSize: 11)),
      onPressed: widget.enabled ? () => widget.conn?.keyCombo(keys) : null,
    );
  }

  Widget _arrowBtn(IconData icon, List<String> keys) {
    return Padding(
      padding: const EdgeInsets.all(2),
      child: IconButton.filledTonal(
        iconSize: 20,
        constraints: const BoxConstraints(minWidth: 40, minHeight: 40),
        onPressed: widget.enabled ? () => widget.conn?.keyCombo(keys) : null,
        icon: Icon(icon),
      ),
    );
  }
}

// ═══════════════════════════════════════════
// FULLSCREEN REMOTE (immersive landscape)
// ═══════════════════════════════════════════
class _FullscreenRemote extends StatefulWidget {
  final PcConnection? conn;
  final int initialMode;
  final ScreenQuality quality;
  const _FullscreenRemote({required this.conn, required this.initialMode, required this.quality});
  @override
  State<_FullscreenRemote> createState() => _FullscreenRemoteState();
}

class _FullscreenRemoteState extends State<_FullscreenRemote> {
  late int _mode;
  bool _showCrosshair = false;

  @override
  void initState() {
    super.initState();
    _mode = widget.initialMode;
    // Force landscape + immersive
    SystemChrome.setPreferredOrientations([
      DeviceOrientation.landscapeLeft,
      DeviceOrientation.landscapeRight,
    ]);
    SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky);
  }

  @override
  void dispose() {
    // Restore portrait + system UI
    SystemChrome.setPreferredOrientations([]);
    SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge);
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final conn = widget.conn;
    if (conn == null) {
      return const Scaffold(
        backgroundColor: Colors.black,
        body: Center(child: Text('No connection', style: TextStyle(color: Colors.white38))),
      );
    }

    return ValueListenableBuilder<ConnectionStatus>(
      valueListenable: conn.statusNotifier,
      builder: (context, status, _) {
        final cs = Theme.of(context).colorScheme;
        final connected = status.connected;
        final hasError = status.error != null;

        return Scaffold(
          backgroundColor: Colors.black,
          resizeToAvoidBottomInset: false,
          body: SafeArea(
            child: Row(
              children: [
                // ── Left: Live preview with Crosshair Overlay ──
                Expanded(
                  flex: 6,
                  child: Container(
                    margin: const EdgeInsets.all(8),
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(color: cs.outlineVariant.withValues(alpha: 0.3)),
                    ),
                    clipBehavior: Clip.antiAlias,
                    child: Stack(
                      children: [
                        InteractiveViewer(
                          minScale: 1.0,
                          maxScale: 6.0,
                          child: ValueListenableBuilder<RTCVideoRenderer?>(
                            valueListenable: conn.webrtcRendererNotifier,
                            builder: (context, renderer, _) {
                              if (renderer != null) {
                                return ScreenPreviewWebRtc(renderer: renderer);
                              }
                              return ValueListenableBuilder<Uint8List?>(
                                valueListenable: conn.screenFrameNotifier,
                                builder: (_, frame, __) {
                                  if (frame == null) {
                                    return Center(child: CircularProgressIndicator(color: cs.primary));
                                  }
                                  return Image.memory(
                                    frame,
                                    gaplessPlayback: true,
                                    fit: BoxFit.contain,
                                    filterQuality: widget.quality == ScreenQuality.best
                                        ? FilterQuality.high
                                        : FilterQuality.medium,
                                    errorBuilder: (context, error, stackTrace) {
                                      return Container(
                                        color: Colors.grey.shade900,
                                        child: const Center(
                                          child: Icon(Icons.broken_image_rounded, color: Colors.white30, size: 36),
                                        ),
                                      );
                                    },
                                  );
                                },
                              );
                            },
                          ),
                        ),
                        if (_showCrosshair)
                          IgnorePointer(
                            child: Center(
                              child: Stack(
                                alignment: Alignment.center,
                                children: [
                                  Container(
                                    width: 28,
                                    height: 28,
                                    decoration: BoxDecoration(
                                      shape: BoxShape.circle,
                                      border: Border.all(color: Colors.redAccent, width: 1.5),
                                    ),
                                  ),
                                  Container(width: 36, height: 1.5, color: Colors.redAccent.withValues(alpha: 0.8)),
                                  Container(width: 1.5, height: 36, color: Colors.redAccent.withValues(alpha: 0.8)),
                                  Container(
                                    width: 4,
                                    height: 4,
                                    decoration: const BoxDecoration(
                                      shape: BoxShape.circle,
                                      color: Colors.redAccent,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                      ],
                    ),
                  ),
                ),

                // ── Right: controls ──
                Expanded(
                  flex: 4,
                  child: Padding(
                    padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
                    child: Column(
                      children: [
                        // Mode toggle + Crosshair + Exit
                        Padding(
                          padding: const EdgeInsets.fromLTRB(4, 8, 8, 4),
                          child: Row(
                            children: [
                              _miniChip('Trackpad', _mode == 0, () => setState(() => _mode = 0), cs),
                              const SizedBox(width: 4),
                              _miniChip('Keyboard', _mode == 1, () => setState(() => _mode = 1), cs),
                              const Spacer(),
                              IconButton(
                                icon: Icon(
                                  _showCrosshair ? Icons.center_focus_strong : Icons.center_focus_weak,
                                  color: _showCrosshair ? Colors.redAccent : Colors.white60,
                                  size: 20,
                                ),
                                tooltip: 'Precision Crosshair',
                                onPressed: () => setState(() => _showCrosshair = !_showCrosshair),
                              ),
                              if (hasError || !connected)
                                IconButton(
                                  icon: const Icon(Icons.error_outline_rounded, color: Colors.redAccent),
                                  tooltip: 'Connection Error',
                                  onPressed: () => _showErrorDialog(context, conn, status),
                                ),
                              IconButton(
                                icon: const Icon(Icons.fullscreen_exit, color: Colors.white70),
                                onPressed: () => Navigator.of(context).pop(),
                                tooltip: 'Exit Fullscreen',
                              ),
                            ],
                          ),
                        ),
                        Expanded(
                          child: Stack(
                            children: [
                              _mode == 0
                                  ? _EmbeddedTrackpad(conn: conn, enabled: connected)
                                  : _EmbeddedKeyboard(conn: conn, enabled: connected),
                              if (!connected)
                                Positioned.fill(
                                                                  child: Container(
                                    margin: const EdgeInsets.only(left: 16, right: 16, bottom: 4),
                                    decoration: BoxDecoration(
                                      color: Colors.black.withValues(alpha: 0.65),
                                      borderRadius: BorderRadius.circular(16),
                                    ),
                                    child: const Center(
                                      child: Column(
                                        mainAxisSize: MainAxisSize.min,
                                        children: [
                                          Icon(Icons.wifi_off_rounded, color: Colors.white54, size: 36),
                                          SizedBox(height: 6),
                                          Text(
                                            'Disconnected',
                                            style: TextStyle(
                                              color: Colors.white70,
                                              fontSize: 13,
                                              fontWeight: FontWeight.w600,
                                            ),
                                          ),
                                        ],
                                      ),
                                    ),
                                  ),
                                ),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
        );
      },
    );
  }

  Widget _miniChip(String label, bool sel, VoidCallback onTap, ColorScheme cs) {
    return GestureDetector(
      onTap: onTap,
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 5),
        decoration: BoxDecoration(
          color: sel ? cs.primaryContainer : Colors.white10,
          borderRadius: BorderRadius.circular(8),
        ),
        child: Text(
          label,
          style: TextStyle(
            fontSize: 11,
            fontWeight: sel ? FontWeight.w600 : FontWeight.normal,
            color: sel ? cs.primary : Colors.white54,
          ),
        ),
      ),
    );
  }
}

void _showErrorDialog(BuildContext context, PcConnection conn, ConnectionStatus status) {
  showDialog(
    context: context,
    builder: (ctx) => AlertDialog(
      title: const Row(
        children: [
          Icon(Icons.error_outline_rounded, color: Colors.redAccent),
          SizedBox(width: 8),
          Text('Connection Error'),
        ],
      ),
      content: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(status.error ?? 'Disconnected from PC. Check if the PC Agent is running and both devices are on the same local network.'),
        ],
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(ctx),
          child: const Text('Dismiss'),
        ),
        FilledButton.icon(
          onPressed: () {
            Navigator.pop(ctx);
            Navigator.push(
              context,
              MaterialPageRoute(
                builder: (_) => DiagnosticsScreen(conn: conn, status: status),
              ),
            );
          },
          icon: const Icon(Icons.network_check_rounded, size: 18),
          label: const Text('Diagnostics'),
        ),
      ],
    ),
  );
}
