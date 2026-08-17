import 'dart:async';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import '../services/connection.dart';

/// Interaction mode for single tap/drag actions on the remote screen preview.
enum ScreenTapMode {
  leftClick(label: 'Click', icon: Icons.touch_app_rounded),
  rightClick(label: 'Right Click', icon: Icons.mouse_rounded),
  doubleClick(label: 'Double Click', icon: Icons.ads_click_rounded),
  dragSelect(label: 'Drag Select', icon: Icons.select_all_rounded),
  hoverMove(label: 'Move Only', icon: Icons.near_me_rounded);

  final String label;
  final IconData icon;
  const ScreenTapMode({required this.label, required this.icon});
}

/// A feature-rich, DPI & aspect-ratio accurate interactive wrapper around screen previews.
///
/// Features:
///  • Aspect-ratio letterbox compensation for 100% precise touch coordinates
///  • Preserves 2-finger pinch zoom & pan with [InteractiveViewer]
///  • 1-finger drag tracking for continuous live cursor control & drag-and-drop selection
///  • Long-press right click & double-tap shortcuts with haptic feedback
///  • Floating mode selector overlay bar (Click, Right Click, Double Click, Drag Select, Hover)
///  • Visual target feedback ripple animation on touch & smooth drag indicator
class InteractiveScreenPreview extends StatefulWidget {
  final Widget child;
  final PcConnection? conn;
  final bool enabled;
  final double aspectRatio;
  final double minScale;
  final double maxScale;
  final bool showModeBar;
  final BoxFit fit;
  final VoidCallback? onToggleFit;

  const InteractiveScreenPreview({
    super.key,
    required this.child,
    required this.conn,
    this.enabled = true,
    this.aspectRatio = 16 / 9,
    this.minScale = 1.0,
    this.maxScale = 6.0,
    this.showModeBar = true,
    this.fit = BoxFit.contain,
    this.onToggleFit,
  });

  @override
  State<InteractiveScreenPreview> createState() => _InteractiveScreenPreviewState();
}

class _InteractiveScreenPreviewState extends State<InteractiveScreenPreview> {
  ScreenTapMode _activeMode = ScreenTapMode.leftClick;
  bool _overlayExpanded = false;
  final TransformationController _transformationController = TransformationController();

  Offset? _touchFeedbackPos;
  int _feedbackKey = 0;
  Timer? _feedbackTimer;

  Offset? _dragIndicatorPos;
  bool _isMousePointerDown = false;
  final Set<int> _activePointers = {};
  bool _wasMultiTouch = false;

  @override
  void dispose() {
    _transformationController.dispose();
    _feedbackTimer?.cancel();
    if (_isMousePointerDown) {
      widget.conn?.mouseButton(button: 'left', action: 'up');
    }
    super.dispose();
  }

  /// Computes (rx, ry) normalized coordinates (0.0 to 1.0) on the actual PC screen,
  /// compensating for aspect-ratio letterboxing/pillarboxing caused by [BoxFit.contain]
  /// or cropping caused by [BoxFit.cover].
  (double, double) _computeNormalizedPos(Offset localPos, Size containerSize) {
    final aspect = widget.aspectRatio > 0 ? widget.aspectRatio : (16 / 9);
    final containerAspect = containerSize.width / containerSize.height;

    double renderW, renderH, offsetX, offsetY;
    if (widget.fit == BoxFit.cover) {
      if (containerAspect > aspect) {
        renderW = containerSize.width;
        renderH = containerSize.width / aspect;
        offsetX = 0;
        offsetY = (containerSize.height - renderH) / 2;
      } else {
        renderH = containerSize.height;
        renderW = containerSize.height * aspect;
        offsetX = (containerSize.width - renderW) / 2;
        offsetY = 0;
      }
    } else {
      if (containerAspect > aspect) {
        renderH = containerSize.height;
        renderW = containerSize.height * aspect;
        offsetX = (containerSize.width - renderW) / 2;
        offsetY = 0;
      } else {
        renderW = containerSize.width;
        renderH = containerSize.width / aspect;
        offsetX = 0;
        offsetY = (containerSize.height - renderH) / 2;
      }
    }

    final relX = localPos.dx - offsetX;
    final relY = localPos.dy - offsetY;

    final rx = (relX / renderW).clamp(0.0, 1.0);
    final ry = (relY / renderH).clamp(0.0, 1.0);
    return (rx, ry);
  }

  void _triggerTapFeedback(Offset pos) {
    _feedbackTimer?.cancel();
    setState(() {
      _touchFeedbackPos = pos;
      _feedbackKey++;
    });

    _feedbackTimer = Timer(const Duration(milliseconds: 320), () {
      if (mounted) {
        setState(() {
          _touchFeedbackPos = null;
        });
      }
    });
  }

  void _handleTapUp(TapUpDetails details) {
    if (!widget.enabled || widget.conn == null || _activePointers.length > 1 || _wasMultiTouch) return;
    final RenderBox? box = context.findRenderObject() as RenderBox?;
    if (box == null || !box.hasSize) return;

    final localPos = details.localPosition;
    final (rx, ry) = _computeNormalizedPos(localPos, box.size);

    _triggerTapFeedback(localPos);
    HapticFeedback.lightImpact();

    switch (_activeMode) {
      case ScreenTapMode.leftClick:
        widget.conn?.tapScreen(xRatio: rx, yRatio: ry);
        break;
      case ScreenTapMode.rightClick:
        widget.conn?.rightClickScreen(xRatio: rx, yRatio: ry);
        break;
      case ScreenTapMode.doubleClick:
        widget.conn?.doubleClickScreen(xRatio: rx, yRatio: ry);
        break;
      case ScreenTapMode.dragSelect:
        // Single tap in dragSelect mode already handled down/up via pointer events
        break;
      case ScreenTapMode.hoverMove:
        widget.conn?.mouseSetNormalized(xRatio: rx, yRatio: ry);
        break;
    }
  }

  void _handleLongPressStart(LongPressStartDetails details) {
    if (!widget.enabled || widget.conn == null || _activePointers.length > 1 || _wasMultiTouch) return;
    final RenderBox? box = context.findRenderObject() as RenderBox?;
    if (box == null || !box.hasSize) return;

    final localPos = details.localPosition;
    final (rx, ry) = _computeNormalizedPos(localPos, box.size);

    _triggerTapFeedback(localPos);
    HapticFeedback.mediumImpact();
    widget.conn?.rightClickScreen(xRatio: rx, yRatio: ry);
  }

  void _onPointerDown(PointerDownEvent event) {
    _activePointers.add(event.pointer);
    if (_activePointers.length > 1) {
      _wasMultiTouch = true;
    }
    if (!widget.enabled || widget.conn == null) return;

    if (_activePointers.length == 1 && !_wasMultiTouch) {
      final RenderBox? box = context.findRenderObject() as RenderBox?;
      if (box != null && box.hasSize) {
        if (_activeMode == ScreenTapMode.dragSelect) {
          final (rx, ry) = _computeNormalizedPos(event.localPosition, box.size);
          widget.conn?.mouseSetNormalized(xRatio: rx, yRatio: ry);
          widget.conn?.mouseButton(button: 'left', action: 'down');
          _isMousePointerDown = true;
          HapticFeedback.selectionClick();
        }
        if (_activeMode == ScreenTapMode.dragSelect || _activeMode == ScreenTapMode.hoverMove) {
          setState(() {
            _dragIndicatorPos = event.localPosition;
          });
        }
      }
    } else {
      // Multi-finger gesture (e.g. pinch zoom / pan) -> cancel drag select state
      if (_isMousePointerDown) {
        widget.conn?.mouseButton(button: 'left', action: 'up');
        _isMousePointerDown = false;
      }
      if (_dragIndicatorPos != null) {
        setState(() {
          _dragIndicatorPos = null;
        });
      }
    }
  }

  void _onPointerMove(PointerMoveEvent event) {
    if (!widget.enabled || widget.conn == null || _wasMultiTouch) return;

    // Only process 1-finger move/drag mouse tracking
    if (_activePointers.length == 1 && (_activeMode == ScreenTapMode.dragSelect || _activeMode == ScreenTapMode.hoverMove)) {
      final RenderBox? box = context.findRenderObject() as RenderBox?;
      if (box == null || !box.hasSize) return;

      final localPos = event.localPosition;
      final (rx, ry) = _computeNormalizedPos(localPos, box.size);

      setState(() {
        _dragIndicatorPos = localPos;
      });
      widget.conn?.mouseSetNormalized(xRatio: rx, yRatio: ry);
    }
  }

  void _onPointerUp(PointerUpEvent event) {
    _activePointers.remove(event.pointer);

    if (_activePointers.isEmpty) {
      if (_isMousePointerDown) {
        widget.conn?.mouseButton(button: 'left', action: 'up');
        _isMousePointerDown = false;
      }
      if (_dragIndicatorPos != null) {
        setState(() {
          _dragIndicatorPos = null;
        });
      }
      Future.microtask(() {
        if (mounted && _activePointers.isEmpty) {
          _wasMultiTouch = false;
        }
      });
    }
  }

  void _onPointerCancel(PointerCancelEvent event) {
    _activePointers.remove(event.pointer);

    if (_activePointers.isEmpty) {
      if (_isMousePointerDown) {
        widget.conn?.mouseButton(button: 'left', action: 'up');
        _isMousePointerDown = false;
      }
      if (_dragIndicatorPos != null) {
        setState(() {
          _dragIndicatorPos = null;
        });
      }
      Future.microtask(() {
        if (mounted && _activePointers.isEmpty) {
          _wasMultiTouch = false;
        }
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return Stack(
      children: [
        // Main Screen Preview inside InteractiveViewer with native 2-finger pinch-zoom
        InteractiveViewer(
          transformationController: _transformationController,
          minScale: widget.minScale,
          maxScale: widget.maxScale,
          panEnabled: true,
          scaleEnabled: true,
          child: Listener(
            onPointerDown: _onPointerDown,
            onPointerMove: _onPointerMove,
            onPointerUp: _onPointerUp,
            onPointerCancel: _onPointerCancel,
            child: GestureDetector(
              behavior: HitTestBehavior.translucent,
              onTapUp: widget.enabled ? _handleTapUp : null,
              onLongPressStart: widget.enabled ? _handleLongPressStart : null,
              child: Stack(
                alignment: Alignment.center,
                children: [
                  widget.child,

                  // Discrete Tap Feedback Ripple (Stationary)
                  if (_touchFeedbackPos != null)
                    Positioned(
                      key: ValueKey(_feedbackKey),
                      left: _touchFeedbackPos!.dx - 20,
                      top: _touchFeedbackPos!.dy - 20,
                      child: IgnorePointer(
                        child: Stack(
                          alignment: Alignment.center,
                          children: [
                            TweenAnimationBuilder<double>(
                              duration: const Duration(milliseconds: 300),
                              tween: Tween<double>(begin: 0.5, end: 1.3),
                              builder: (context, val, _) {
                                return Opacity(
                                  opacity: (1.3 - val).clamp(0.0, 1.0),
                                  child: Container(
                                    width: 40 * val,
                                    height: 40 * val,
                                    decoration: BoxDecoration(
                                      shape: BoxShape.circle,
                                      border: Border.all(color: cs.primary, width: 2.0),
                                      color: cs.primary.withValues(alpha: 0.25),
                                    ),
                                  ),
                                );
                              },
                            ),
                            Container(
                              width: 6,
                              height: 6,
                              decoration: BoxDecoration(
                                shape: BoxShape.circle,
                                color: cs.primary,
                                boxShadow: [
                                  BoxShadow(
                                    color: cs.primary.withValues(alpha: 0.8),
                                    blurRadius: 8,
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),

                  // Continuous Drag / Hover Pointer Indicator
                  if (_dragIndicatorPos != null && _touchFeedbackPos == null)
                    Positioned(
                      left: _dragIndicatorPos!.dx - 8,
                      top: _dragIndicatorPos!.dy - 8,
                      child: IgnorePointer(
                        child: Container(
                          width: 16,
                          height: 16,
                          decoration: BoxDecoration(
                            shape: BoxShape.circle,
                            color: cs.primary.withValues(alpha: 0.35),
                            border: Border.all(color: cs.primary, width: 1.5),
                          ),
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),

        // Floating Tap Mode Selection Overlay
        if (widget.showModeBar && widget.enabled)
          Positioned(
            top: 8,
            right: 8,
            child: AnimatedContainer(
              duration: const Duration(milliseconds: 200),
              curve: Curves.easeOutCubic,
              padding: const EdgeInsets.all(4),
              decoration: BoxDecoration(
                color: Colors.black.withValues(alpha: 0.75),
                borderRadius: BorderRadius.circular(12),
                border: Border.all(color: Colors.white12),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (widget.onToggleFit != null) ...[
                    InkWell(
                      onTap: () {
                        HapticFeedback.selectionClick();
                        widget.onToggleFit!();
                      },
                      borderRadius: BorderRadius.circular(8),
                      child: Container(
                        padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
                        decoration: BoxDecoration(
                          color: widget.fit == BoxFit.cover
                              ? cs.primary.withValues(alpha: 0.25)
                              : Colors.transparent,
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Icon(
                              widget.fit == BoxFit.cover ? Icons.crop_free_rounded : Icons.fit_screen_rounded,
                              size: 14,
                              color: widget.fit == BoxFit.cover ? cs.primary : Colors.white70,
                            ),
                            const SizedBox(width: 3),
                            Text(
                              widget.fit == BoxFit.cover ? 'Fill' : 'Fit',
                              style: TextStyle(
                                fontSize: 10,
                                fontWeight: FontWeight.bold,
                                color: widget.fit == BoxFit.cover ? cs.primary : Colors.white70,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(width: 4),
                  ],
                  if (_overlayExpanded) ...[
                    ...ScreenTapMode.values.map((mode) {
                      final active = mode == _activeMode;
                      return Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 2),
                        child: InkWell(
                          onTap: () {
                            HapticFeedback.selectionClick();
                            setState(() {
                              _activeMode = mode;
                              _overlayExpanded = false;
                            });
                          },
                          borderRadius: BorderRadius.circular(8),
                          child: Container(
                            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                            decoration: BoxDecoration(
                              color: active ? cs.primary : Colors.transparent,
                              borderRadius: BorderRadius.circular(8),
                            ),
                            child: Row(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                Icon(mode.icon, size: 14, color: active ? cs.onPrimary : Colors.white70),
                                const SizedBox(width: 4),
                                Text(
                                  mode.label,
                                  style: TextStyle(
                                    fontSize: 11,
                                    fontWeight: active ? FontWeight.bold : FontWeight.normal,
                                    color: active ? cs.onPrimary : Colors.white70,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ),
                      );
                    }),
                    const SizedBox(width: 4),
                  ],
                  InkWell(
                    onTap: () {
                      HapticFeedback.selectionClick();
                      setState(() => _overlayExpanded = !_overlayExpanded);
                    },
                    borderRadius: BorderRadius.circular(8),
                    child: Container(
                      padding: const EdgeInsets.all(4),
                      child: Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Icon(_activeMode.icon, size: 16, color: cs.primary),
                          const SizedBox(width: 2),
                          Icon(
                            _overlayExpanded ? Icons.chevron_right_rounded : Icons.tune_rounded,
                            size: 14,
                            color: Colors.white54,
                          ),
                        ],
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
      ],
    );
  }
}
