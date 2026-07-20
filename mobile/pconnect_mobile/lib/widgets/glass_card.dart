import 'dart:ui';
import 'package:flutter/material.dart';
import '../constants/theme_tokens.dart';

/// Elevated glassmorphic card component with smooth spring press scaling
class GlassCard extends StatefulWidget {
  final Widget child;
  final EdgeInsetsGeometry? padding;
  final EdgeInsetsGeometry? margin;
  final VoidCallback? onTap;
  final Color? backgroundColor;
  final Color? borderColor;
  final double borderRadius;
  final bool enableBlur;
  final double blurAmount;

  const GlassCard({
    super.key,
    required this.child,
    this.padding = const EdgeInsets.all(16),
    this.margin,
    this.onTap,
    this.backgroundColor,
    this.borderColor,
    this.borderRadius = 16.0,
    this.enableBlur = false,
    this.blurAmount = 12.0,
  });

  @override
  State<GlassCard> createState() => _GlassCardState();
}

class _GlassCardState extends State<GlassCard> {
  bool _isPressed = false;

  @override
  Widget build(BuildContext context) {
    final isDark = Theme.of(context).brightness == Brightness.dark;
    final bg = widget.backgroundColor ??
        (isDark ? AppColors.bgElevated1 : AppColors.bgElevated1Light);
    final border = widget.borderColor ??
        (isDark ? AppColors.borderSubtle : AppColors.borderSubtleLight);

    Widget cardContent = Container(
      padding: widget.padding,
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(widget.borderRadius),
        border: Border.all(color: border, width: 1.0),
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [
            Colors.white.withValues(alpha: isDark ? 0.04 : 0.6),
            Colors.white.withValues(alpha: isDark ? 0.0 : 0.2),
          ],
        ),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: isDark ? 0.24 : 0.06),
            blurRadius: 12,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: widget.child,
    );

    if (widget.enableBlur) {
      cardContent = ClipRRect(
        borderRadius: BorderRadius.circular(widget.borderRadius),
        child: BackdropFilter(
          filter: ImageFilter.blur(
            sigmaX: widget.blurAmount,
            sigmaY: widget.blurAmount,
          ),
          child: cardContent,
        ),
      );
    }

    if (widget.onTap == null) {
      return Padding(
        padding: widget.margin ?? EdgeInsets.zero,
        child: cardContent,
      );
    }

    return Padding(
      padding: widget.margin ?? EdgeInsets.zero,
      child: GestureDetector(
        onTapDown: (_) => setState(() => _isPressed = true),
        onTapUp: (_) => setState(() => _isPressed = false),
        onTapCancel: () => setState(() => _isPressed = false),
        onTap: widget.onTap,
        child: AnimatedScale(
          scale: _isPressed ? AppMotion.pressScale : 1.0,
          duration: AppMotion.durationFast,
          curve: AppMotion.easeStandard,
          child: cardContent,
        ),
      ),
    );
  }
}
