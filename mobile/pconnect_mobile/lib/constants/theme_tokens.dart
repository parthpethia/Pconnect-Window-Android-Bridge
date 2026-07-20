import 'package:flutter/material.dart';

/// Design System Tokens for Pconnect Mobile
abstract class AppColors {
  // Core Accent & Glow
  static const Color primary = Color(0xFF6C5CE7);
  static const Color primaryHover = Color(0xFF5A4BD1);
  static const Color primaryPressed = Color(0xFF4B3FB8);
  static const Color primaryGlow = Color(0x596C5CE7); // 35% opacity halo

  // Dark Theme Surfaces
  static const Color bgBase = Color(0xFF121216);
  static const Color bgElevated1 = Color(0xFF1A1A20); // Cards
  static const Color bgElevated2 = Color(0xFF232330); // Modals, Sheets
  static const Color bgElevated3 = Color(0xFF2C2C3A); // Tooltips, Popovers

  // Borders
  static const Color borderSubtle = Color(0x0FFFFFFF); // 6% white
  static const Color borderStrong = Color(0x1FFFFFFF); // 12% white

  // Text Tokens (Dark Theme)
  static const Color textPrimary = Color(0xFFF8F9FA);
  static const Color textSecondary = Color(0xFFA0A0B0);
  static const Color textDisabled = Color(0xFF5C5C68);

  // Semantic Colors
  static const Color success = Color(0xFF00B894);
  static const Color warning = Color(0xFFFDCB6E);
  static const Color danger = Color(0xFFFF7675);
  static const Color info = Color(0xFF74B9FF);

  // Semantic Background Tints (16% opacity)
  static const Color successTint = Color(0x2900B894);
  static const Color warningTint = Color(0x29FDCB6E);
  static const Color dangerTint = Color(0x29FF7675);
  static const Color infoTint = Color(0x2974B9FF);
  static const Color primaryTint = Color(0x296C5CE7);

  // Light Theme Mirrors
  static const Color bgBaseLight = Color(0xFFF5F6FA);
  static const Color bgElevated1Light = Color(0xFFFFFFFF);
  static const Color textPrimaryLight = Color(0xFF1A1A20);
  static const Color borderSubtleLight = Color(0x0D000000);
}

abstract class AppMotion {
  // Transition Durations
  static const Duration durationFast = Duration(milliseconds: 150);
  static const Duration durationStandard = Duration(milliseconds: 220);
  static const Duration durationEmphasized = Duration(milliseconds: 350);
  static const Duration durationSlow = Duration(milliseconds: 500);

  // Easing Curves
  static const Curve easeStandard = Cubic(0.2, 0.0, 0.0, 1.0);
  static const Curve easeEmphasized = Cubic(0.05, 0.7, 0.1, 1.0);
  static const Curve easeSpring = ElasticOutCurve(0.8);

  // Button Press Scale Factor
  static const double pressScale = 0.97;
}

abstract class AppTypography {
  static const TextStyle display = TextStyle(
    fontSize: 28,
    fontWeight: FontWeight.w700,
    letterSpacing: -0.5,
  );

  static const TextStyle title = TextStyle(
    fontSize: 20,
    fontWeight: FontWeight.w600,
    letterSpacing: -0.2,
  );

  static const TextStyle body = TextStyle(
    fontSize: 15,
    fontWeight: FontWeight.w400,
  );

  static const TextStyle label = TextStyle(
    fontSize: 13,
    fontWeight: FontWeight.w500,
  );

  static const TextStyle caption = TextStyle(
    fontSize: 11,
    fontWeight: FontWeight.w400,
  );
}
