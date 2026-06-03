import 'dart:async';
import 'package:flutter/material.dart';

class WelcomeScreen extends StatefulWidget {
  final VoidCallback onGetStarted;

  const WelcomeScreen({super.key, required this.onGetStarted});

  @override
  State<WelcomeScreen> createState() => _WelcomeScreenState();
}

class _WelcomeScreenState extends State<WelcomeScreen>
    with SingleTickerProviderStateMixin {
  late AnimationController _animController;
  late Animation<double> _pulseAnimation;
  int _currentFeatureIndex = 0;
  late Timer _featureTimer;

  final List<Map<String, dynamic>> _features = [
    {
      'icon': Icons.mouse_rounded,
      'title': 'Remote Input Controls',
      'desc': 'Navigate your PC screen with fluid mouse trackpad controls and specialized virtual keyboard features.'
    },
    {
      'icon': Icons.copy_all_rounded,
      'title': 'Instant Clipboard Bridge',
      'desc': 'Seamless automatic clipboard synchronization between your Android device and Windows PC.'
    },
    {
      'icon': Icons.swap_horizontal_circle_rounded,
      'title': 'High-Speed File Share',
      'desc': 'Transfer photos, videos, and arbitrary documents locally with a simple tap on your screen.'
    },
    {
      'icon': Icons.screen_share_rounded,
      'title': 'Sleek Desktop Mirroring',
      'desc': 'Stream your active Windows screen in real-time right onto your phone with low latency.'
    },
  ];

  @override
  void initState() {
    super.initState();
    _animController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 4),
    )..repeat(reverse: true);

    _pulseAnimation = Tween<double>(begin: 0.95, end: 1.05).animate(
      CurvedAnimation(parent: _animController, curve: Curves.easeInOut),
    );

    // Auto rotate feature showcase every 3.5 seconds
    _featureTimer = Timer.periodic(const Duration(milliseconds: 3500), (timer) {
      if (mounted) {
        setState(() {
          _currentFeatureIndex = (_currentFeatureIndex + 1) % _features.length;
        });
      }
    });
  }

  @override
  void dispose() {
    _animController.dispose();
    _featureTimer.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      body: Stack(
        children: [
          // ── Beautiful Animated Gradient Background ──
          AnimatedBuilder(
            animation: _animController,
            builder: (context, child) {
              return Container(
                decoration: BoxDecoration(
                  gradient: LinearGradient(
                    begin: Alignment.topLeft,
                    end: Alignment.bottomRight,
                    colors: [
                      Color.lerp(const Color(0xFF1E1B4B), const Color(0xFF0F172A), _animController.value)!,
                      Color.lerp(const Color(0xFF311042), const Color(0xFF111827), _animController.value)!,
                      Color.lerp(const Color(0xFF0F172A), const Color(0xFF1E1B4B), _animController.value)!,
                    ],
                  ),
                ),
              );
            },
          ),

          // ── Floating Decorative Node Network Background (Neon Vibe) ──
          Positioned.fill(
            child: AnimatedBuilder(
              animation: _animController,
              builder: (context, child) {
                return CustomPaint(
                  painter: _NodeMeshPainter(
                    animationProgress: _animController.value,
                    primaryColor: cs.primary.withOpacity(0.12),
                    secondaryColor: cs.secondary.withOpacity(0.12),
                  ),
                );
              },
            ),
          ),

          // ── Core Layout ──
          SafeArea(
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 24.0, vertical: 16.0),
              child: Column(
                children: [
                  const Spacer(flex: 2),

                  // ── Logo and App Branding ──
                  ScaleTransition(
                    scale: _pulseAnimation,
                    child: Center(
                      child: Container(
                        width: 130,
                        height: 130,
                        decoration: BoxDecoration(
                          shape: BoxShape.circle,
                          boxShadow: [
                            BoxShadow(
                              color: cs.primary.withOpacity(0.4),
                              blurRadius: 36,
                              spreadRadius: 4,
                            ),
                          ],
                        ),
                        child: CustomPaint(
                          painter: _PconnectLogoPainter(
                            accentColor: cs.primary,
                            glowColor: cs.tertiary,
                          ),
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 32),

                  // ── App Title & Headline ──
                  const Text(
                    'PCONNECT',
                    style: TextStyle(
                      fontFamily: 'Outfit',
                      fontSize: 38,
                      fontWeight: FontWeight.w900,
                      letterSpacing: 8.0,
                      color: Colors.white,
                      shadows: [
                        Shadow(
                          color: Colors.black38,
                          offset: Offset(0, 4),
                          blurRadius: 12,
                        ),
                        Shadow(
                          color: Color(0xFF6C5CE7),
                          offset: Offset(0, 0),
                          blurRadius: 18,
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'The Ultimate PC-Android Wireless Bridge',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontFamily: 'Inter',
                      fontSize: 15,
                      fontWeight: FontWeight.w400,
                      letterSpacing: 0.5,
                      color: Colors.white.withOpacity(0.7),
                    ),
                  ),

                  const Spacer(flex: 3),

                  // ── Premium Feature Swapper (Interactive Highlight Carousel) ──
                  SizedBox(
                    height: 165,
                    width: double.infinity,
                    child: AnimatedSwitcher(
                      duration: const Duration(milliseconds: 600),
                      transitionBuilder: (child, anim) {
                        return FadeTransition(
                          opacity: anim,
                          child: SlideTransition(
                            position: Tween<Offset>(
                              begin: const Offset(0.0, 0.15),
                              end: Offset.zero,
                            ).animate(anim),
                            child: child,
                          ),
                        );
                      },
                      child: Container(
                        key: ValueKey<int>(_currentFeatureIndex),
                        padding: const EdgeInsets.all(20),
                        decoration: BoxDecoration(
                          color: Colors.white.withOpacity(0.06),
                          borderRadius: BorderRadius.circular(24),
                          border: Border.all(
                            color: Colors.white.withOpacity(0.12),
                          ),
                        ),
                        child: Row(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Container(
                              padding: const EdgeInsets.all(12),
                              decoration: BoxDecoration(
                                color: cs.primaryContainer.withOpacity(0.3),
                                shape: BoxShape.circle,
                              ),
                              child: Icon(
                                _features[_currentFeatureIndex]['icon'] as IconData,
                                color: cs.onPrimaryContainer,
                                size: 30,
                              ),
                            ),
                            const SizedBox(width: 16),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  Text(
                                    _features[_currentFeatureIndex]['title'] as String,
                                    style: const TextStyle(
                                      color: Colors.white,
                                      fontSize: 16,
                                      fontWeight: FontWeight.w700,
                                      letterSpacing: 0.5,
                                    ),
                                  ),
                                  const SizedBox(height: 6),
                                  Text(
                                    _features[_currentFeatureIndex]['desc'] as String,
                                    style: TextStyle(
                                      color: Colors.white.withOpacity(0.65),
                                      fontSize: 13,
                                      height: 1.45,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),

                  const SizedBox(height: 16),

                  // ── Indicator Dots ──
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: List.generate(
                      _features.length,
                      (index) => GestureDetector(
                        onTap: () {
                          setState(() {
                            _currentFeatureIndex = index;
                          });
                        },
                        child: AnimatedContainer(
                          duration: const Duration(milliseconds: 300),
                          margin: const EdgeInsets.symmetric(horizontal: 4.0),
                          height: 7,
                          width: _currentFeatureIndex == index ? 24 : 7,
                          decoration: BoxDecoration(
                            color: _currentFeatureIndex == index
                                ? cs.primary
                                : Colors.white24,
                            borderRadius: BorderRadius.circular(4),
                          ),
                        ),
                      ),
                    ),
                  ),

                  const Spacer(flex: 3),

                  // ── Premium CTA Action Button ──
                  Container(
                    width: double.infinity,
                    height: 58,
                    decoration: BoxDecoration(
                      borderRadius: BorderRadius.circular(30),
                      gradient: LinearGradient(
                        colors: [
                          cs.primary,
                          const Color(0xFF8E2DE2),
                        ],
                      ),
                      boxShadow: [
                        BoxShadow(
                          color: cs.primary.withOpacity(0.4),
                          blurRadius: 18,
                          offset: const Offset(0, 6),
                        ),
                      ],
                    ),
                    child: ElevatedButton(
                      onPressed: widget.onGetStarted,
                      style: ElevatedButton.styleFrom(
                        backgroundColor: Colors.transparent,
                        foregroundColor: Colors.white,
                        shadowColor: Colors.transparent,
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(30),
                        ),
                      ),
                      child: const Row(
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          Text(
                            'GET STARTED',
                            style: TextStyle(
                              fontSize: 16,
                              fontWeight: FontWeight.w800,
                              letterSpacing: 1.5,
                            ),
                          ),
                          SizedBox(width: 10),
                          Icon(
                            Icons.arrow_forward_rounded,
                            size: 20,
                          ),
                        ],
                      ),
                    ),
                  ),
                  const SizedBox(height: 16),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

// ── Custom Paint for Beautiful Animated Logo ──
class _PconnectLogoPainter extends CustomPainter {
  final Color accentColor;
  final Color glowColor;

  _PconnectLogoPainter({required this.accentColor, required this.glowColor});

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final outerRadius = size.width / 2;

    // Glowing base gradient
    final basePaint = Paint()
      ..shader = RadialGradient(
        colors: [
          accentColor.withOpacity(0.9),
          glowColor.withOpacity(0.7),
          const Color(0xFF2E0854).withOpacity(0.9),
        ],
      ).createShader(Rect.fromCircle(center: center, radius: outerRadius))
      ..style = PaintingStyle.fill;

    canvas.drawCircle(center, outerRadius, basePaint);

    // Glowing border rings
    final borderPaint = Paint()
      ..color = Colors.white.withOpacity(0.2)
      ..strokeWidth = 1.5
      ..style = PaintingStyle.stroke;
    canvas.drawCircle(center, outerRadius - 3, borderPaint);

    final pulsePaint = Paint()
      ..color = Colors.white.withOpacity(0.4)
      ..strokeWidth = 2.0
      ..style = PaintingStyle.stroke;
    canvas.drawCircle(center, outerRadius - 12, pulsePaint);

    // Stylized interconnected network visual
    final linePaint = Paint()
      ..color = Colors.white.withOpacity(0.4)
      ..strokeWidth = 1.0;

    // Center Connection Hub Node
    final nodePaint = Paint()
      ..color = Colors.white
      ..style = PaintingStyle.fill;

    // Orbital ring
    canvas.drawCircle(center, outerRadius * 0.45, borderPaint);

    // Draw the clean vector letter "P" styled uniquely in the middle
    final textPainter = TextPainter(
      textDirection: TextDirection.ltr,
    );

    textPainter.text = const TextSpan(
      text: 'P',
      style: TextStyle(
        fontSize: 54,
        fontFamily: 'Outfit',
        fontWeight: FontWeight.w900,
        color: Colors.white,
        shadows: [
          Shadow(
            color: Colors.black54,
            offset: Offset(0, 4),
            blurRadius: 8,
          ),
        ],
      ),
    );

    textPainter.layout();
    textPainter.paint(
      canvas,
      Offset(center.dx - textPainter.width / 2, center.dy - textPainter.height / 2 - 2),
    );

    // Draw connection anchor dots
    canvas.drawCircle(Offset(center.dx + outerRadius * 0.45, center.dy), 4, nodePaint);
    canvas.drawCircle(Offset(center.dx - outerRadius * 0.32, center.dy - outerRadius * 0.32), 3, nodePaint);
    canvas.drawCircle(Offset(center.dx - outerRadius * 0.32, center.dy + outerRadius * 0.32), 3, nodePaint);

    canvas.drawLine(
      Offset(center.dx + outerRadius * 0.45, center.dy),
      Offset(center.dx - outerRadius * 0.32, center.dy - outerRadius * 0.32),
      linePaint,
    );
    canvas.drawLine(
      Offset(center.dx + outerRadius * 0.45, center.dy),
      Offset(center.dx - outerRadius * 0.32, center.dy + outerRadius * 0.32),
      linePaint,
    );
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

// ── Background Abstract Nodes Matrix Painter ──
class _NodeMeshPainter extends CustomPainter {
  final double animationProgress;
  final Color primaryColor;
  final Color secondaryColor;

  _NodeMeshPainter({
    required this.animationProgress,
    required this.primaryColor,
    required this.secondaryColor,
  });

  @override
  void paint(Canvas canvas, Size size) {
    final width = size.width;
    final height = size.height;

    final paintLine = Paint()..strokeWidth = 1.0;
    final paintNode = Paint()..style = PaintingStyle.fill;

    // Fixed points but floating slightly with animationProgress
    final List<Offset> points = [
      Offset(width * 0.15, height * 0.15 + (animationProgress * 15)),
      Offset(width * 0.85, height * 0.22 - (animationProgress * 20)),
      Offset(width * 0.35, height * 0.48 + (animationProgress * 25)),
      Offset(width * 0.75, height * 0.58 - (animationProgress * 18)),
      Offset(width * 0.20, height * 0.80 + (animationProgress * 22)),
      Offset(width * 0.80, height * 0.78 - (animationProgress * 15)),
      Offset(width * 0.50, height * 0.90 + (animationProgress * 10)),
      Offset(width * 0.50, height * 0.10 - (animationProgress * 8)),
    ];

    // Draw connecting lines
    for (int i = 0; i < points.length; i++) {
      for (int j = i + 1; j < points.length; j++) {
        final dist = (points[i] - points[j]).distance;
        if (dist < width * 0.6) {
          final opacity = (1.0 - (dist / (width * 0.6))).clamp(0.0, 1.0);
          paintLine.color = Color.lerp(primaryColor, secondaryColor, (i + j) / (points.length * 2))!
              .withOpacity(opacity * 0.3);
          canvas.drawLine(points[i], points[j], paintLine);
        }
      }
    }

    // Draw glowing nodes
    for (int i = 0; i < points.length; i++) {
      paintNode.color = (i % 2 == 0 ? primaryColor : secondaryColor).withOpacity(0.55);
      canvas.drawCircle(points[i], 5.0 + (i % 3), paintNode);
      canvas.drawCircle(points[i], 12.0 + (i % 3) * 3, Paint()
        ..color = paintNode.color.withOpacity(0.15)
        ..style = PaintingStyle.fill);
    }
  }

  @override
  bool shouldRepaint(covariant _NodeMeshPainter oldDelegate) =>
      oldDelegate.animationProgress != animationProgress;
}
