import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';
import '../constants/theme_tokens.dart';
import 'glass_card.dart';

/// Reusable collapsible card container with animated size expansion & SharedPreferences persistence
class CollapsibleSection extends StatefulWidget {
  final String title;
  final IconData? icon;
  final Widget child;
  final Widget? trailing;
  final String? storageKey;
  final bool defaultExpanded;

  const CollapsibleSection({
    super.key,
    required this.title,
    required this.child,
    this.icon,
    this.trailing,
    this.storageKey,
    this.defaultExpanded = true,
  });

  @override
  State<CollapsibleSection> createState() => _CollapsibleSectionState();
}

class _CollapsibleSectionState extends State<CollapsibleSection> {
  late bool _isExpanded;

  @override
  void initState() {
    super.initState();
    _isExpanded = widget.defaultExpanded;
    _loadState();
  }

  Future<void> _loadState() async {
    if (widget.storageKey == null) return;
    final prefs = await SharedPreferences.getInstance();
    final val = prefs.getBool('section_${widget.storageKey}');
    if (val != null && mounted) {
      setState(() => _isExpanded = val);
    }
  }

  Future<void> _toggle() async {
    final next = !_isExpanded;
    setState(() => _isExpanded = next);
    if (widget.storageKey != null) {
      final prefs = await SharedPreferences.getInstance();
      await prefs.setBool('section_${widget.storageKey}', next);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return GlassCard(
      padding: EdgeInsets.zero,
      margin: const EdgeInsets.only(bottom: 16),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          // Section Header
          InkWell(
            onTap: _toggle,
            borderRadius: BorderRadius.circular(16),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
              child: Row(
                children: [
                  if (widget.icon != null) ...[
                    Icon(widget.icon, size: 20, color: AppColors.primary),
                    const SizedBox(width: 10),
                  ],
                  Expanded(
                    child: Text(
                      widget.title,
                      style: AppTypography.title.copyWith(
                        fontSize: 16,
                        color: theme.colorScheme.onSurface,
                      ),
                    ),
                  ),
                  if (widget.trailing != null) widget.trailing!,
                  const SizedBox(width: 8),
                  AnimatedRotation(
                    turns: _isExpanded ? 0.5 : 0.0,
                    duration: AppMotion.durationStandard,
                    curve: AppMotion.easeStandard,
                    child: Icon(
                      Icons.keyboard_arrow_down_rounded,
                      color: AppColors.textSecondary,
                    ),
                  ),
                ],
              ),
            ),
          ),
          // Expandable Body
          AnimatedCrossFade(
            firstChild: const SizedBox(width: double.infinity),
            secondChild: Padding(
              padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
              child: widget.child,
            ),
            crossFadeState: _isExpanded
                ? CrossFadeState.showSecond
                : CrossFadeState.showFirst,
            duration: AppMotion.durationStandard,
            firstCurve: AppMotion.easeStandard,
            secondCurve: AppMotion.easeStandard,
          ),
        ],
      ),
    );
  }
}
