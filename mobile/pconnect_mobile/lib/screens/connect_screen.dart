import 'dart:async';
import 'dart:convert';
import 'package:flutter/material.dart';
import 'package:mobile_scanner/mobile_scanner.dart';
import 'package:permission_handler/permission_handler.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../services/connection.dart';
import 'diagnostics_screen.dart';
import 'discovery_screen.dart'; // To reuse ProfileStore and DiscoveredPc structures if needed, or we declare them

// We can import or declare the ConnectionProfile / ProfileStore to avoid duplication.
// Let's reuse them from discovery_screen.dart, but to ensure robust separation and avoid dependency problems,
// let's make sure ProfileStore is imported. It is defined in discovery_screen.dart. Let's make sure it is fully compatible.

class ConnectScreen extends StatefulWidget {
  final String deviceId;
  final ConnectionStatus status;
  final Future<void> Function(String host, int port, {int? wssPort}) onConnect;
  final Future<bool> Function(String code) onPair;
  final VoidCallback? onCancel;
  final PcConnection? conn;

  const ConnectScreen({
    super.key,
    required this.deviceId,
    required this.status,
    required this.onConnect,
    required this.onPair,
    this.onCancel,
    this.conn,
  });

  @override
  State<ConnectScreen> createState() => _ConnectScreenState();
}

class _ConnectScreenState extends State<ConnectScreen> with SingleTickerProviderStateMixin {
  final _ipController = TextEditingController();
  final _portController = TextEditingController(text: kWsPortDefault.toString());
  final _codeController = TextEditingController();

  late AnimationController _pulseController;
  late Animation<double> _logoScale;

  List<DiscoveredPc> _discovered = [];
  List<ConnectionProfile> _profiles = [];
  bool _scanning = false;
  bool _connecting = false;
  String? _connError;

  @override
  void initState() {
    super.initState();
    _pulseController = AnimationController(
      vsync: this,
      duration: const Duration(seconds: 3),
    )..repeat(reverse: true);

    _logoScale = Tween<double>(begin: 0.96, end: 1.04).animate(
      CurvedAnimation(parent: _pulseController, curve: Curves.easeInOut),
    );

    _loadProfiles();
    _scanNetwork();
  }

  @override
  void didUpdateWidget(ConnectScreen old) {
    super.didUpdateWidget(old);
    // Clear connecting state once status updates
    if (widget.status.connected || widget.status.error != null || widget.status.needsPairing) {
      if (mounted) {
        setState(() {
          _connecting = false;
          _connError = widget.status.error;
        });
      }
    }
  }

  @override
  void dispose() {
    _pulseController.dispose();
    _ipController.dispose();
    _portController.dispose();
    _codeController.dispose();
    super.dispose();
  }

  Future<void> _loadProfiles() async {
    final profiles = await ProfileStore.load();
    if (mounted) setState(() => _profiles = profiles);
  }

  Future<void> _scanNetwork() async {
    if (_scanning) return;
    setState(() => _scanning = true);
    try {
      final results = await DiscoveryClient.discover(timeout: const Duration(seconds: 4));
      if (mounted) setState(() => _discovered = results);
    } catch (e) {
      if (mounted) {
        showDialog(
          context: context,
          builder: (ctx) => AlertDialog(
            title: const Row(
              children: [
                Icon(Icons.error_outline_rounded, color: Colors.redAccent),
                SizedBox(width: 8),
                Text('Discovery Error'),
              ],
            ),
            content: Text('Could not start local network discovery.\n\nDetails: ${e.toString().replaceAll('Exception: ', '')}'),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(ctx),
                child: const Text('Dismiss'),
              ),
            ],
          ),
        );
      }
    }
    if (mounted) setState(() => _scanning = false);
  }

  Future<void> _connectTo(String host, int port, {int? wssPort}) async {
    setState(() {
      _connecting = true;
      _connError = null;
    });

    try {
      await widget.onConnect(host, port, wssPort: wssPort);
      // Save profile
      await ProfileStore.upsert(ConnectionProfile(
        name: host,
        ip: host,
        port: port,
        wssPort: wssPort,
        lastConnected: DateTime.now(),
      ));
      await _loadProfiles();
    } catch (e) {
      setState(() {
        _connecting = false;
        _connError = e.toString();
      });
    }
  }

  Future<void> _submitCode() async {
    final code = _codeController.text.trim();
    if (code.isEmpty) return;
    final ok = await widget.onPair(code);
    if (!mounted) return;
    if (ok) {
      _codeController.clear();
    } else {
      final msg = widget.status.error ??
          'Pairing failed. Confirm the 6-digit code on your PC (it rotates every 5 minutes).';
      setState(() => _connError = msg);
      ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(msg)));
    }
  }

  void _openQrScanner() {
    Navigator.of(context).push(MaterialPageRoute(
      builder: (_) => _ModernQrScanPage(
        onResult: (ip, port, wssPort, code) {
          if (ip == '0.0.0.0' || ip.startsWith('127.')) {
            ScaffoldMessenger.of(context).showSnackBar(
              const SnackBar(content: Text('QR code has no valid PC IP. Enter the address manually.')),
            );
            return;
          }
          Navigator.of(context).pop();
          _ipController.text = ip;
          _portController.text = port.toString();
          _connectTo(ip, port, wssPort: wssPort ?? kDefaultWssPort);
          if (code != null && code.isNotEmpty) {
            Future<void>.delayed(const Duration(milliseconds: 800), () async {
              if (!mounted) return;
              await widget.onPair(code);
            });
          }
        },
      ),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      backgroundColor: const Color(0xFF0F172A),
      body: Stack(
        children: [
          // ── Mesh gradient circles ──
          Positioned(
            top: -100,
            right: -100,
            child: Container(
              width: 300,
              height: 300,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: cs.primary.withOpacity(0.18),
              ),
            ),
          ),
          Positioned(
            bottom: -80,
            left: -80,
            child: Container(
              width: 250,
              height: 250,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: cs.secondary.withOpacity(0.12),
              ),
            ),
          ),

          // ── Diagnostics Floating Button ──
          Positioned(
            top: 12,
            right: 12,
            child: SafeArea(
              child: Tooltip(
                message: 'Connection Diagnostics',
                child: CircleAvatar(
                  backgroundColor: Colors.white.withOpacity(0.06),
                  foregroundColor: Colors.white70,
                  child: IconButton(
                    icon: const Icon(Icons.network_check_rounded),
                    onPressed: () {
                      Navigator.push(
                        context,
                        MaterialPageRoute(
                          builder: (_) => DiagnosticsScreen(
                            conn: widget.conn,
                            status: widget.status,
                          ),
                        ),
                      );
                    },
                  ),
                ),
              ),
            ),
          ),

          // ── Scrollable Body ──
          SafeArea(
            child: CustomScrollView(
              slivers: [
                // Top App Header
                SliverToBoxAdapter(
                  child: Padding(
                    padding: const EdgeInsets.symmetric(horizontal: 20.0, vertical: 24.0),
                    child: Column(
                      children: [
                        // Animated pulsing logo
                        ScaleTransition(
                          scale: _logoScale,
                          child: Container(
                            width: 80,
                            height: 80,
                            decoration: BoxDecoration(
                              shape: BoxShape.circle,
                              boxShadow: [
                                BoxShadow(
                                  color: cs.primary.withOpacity(0.3),
                                  blurRadius: 20,
                                ),
                              ],
                            ),
                            child: CustomPaint(
                              painter: _MiniLogoPainter(accentColor: cs.primary),
                            ),
                          ),
                        ),
                        const SizedBox(height: 16),
                        const Text(
                          'Connect to PC',
                          style: TextStyle(
                            fontFamily: 'Outfit',
                            fontSize: 26,
                            fontWeight: FontWeight.w800,
                            color: Colors.white,
                          ),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          'Make sure Pconnect Agent is running on your computer',
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            color: Colors.white.withOpacity(0.5),
                            fontSize: 13,
                          ),
                        ),
                      ],
                    ),
                  ),
                ),

                // Main Connection Controls
                SliverPadding(
                  padding: const EdgeInsets.symmetric(horizontal: 20.0),
                  sliver: SliverList(
                    delegate: SliverChildListDelegate([
                      // ── Pairing Box ──
                      if (widget.status.needsPairing) ...[
                        Card(
                          color: const Color(0xFF1E1B4B),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(20),
                            side: BorderSide(color: cs.primary.withOpacity(0.4)),
                          ),
                          child: Padding(
                            padding: const EdgeInsets.all(20),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Row(
                                  children: [
                                    Icon(Icons.vpn_key_rounded, color: cs.primary),
                                    const SizedBox(width: 10),
                                    const Text(
                                      'Security Pair Required',
                                      style: TextStyle(
                                        color: Colors.white,
                                        fontWeight: FontWeight.w700,
                                        fontSize: 16,
                                      ),
                                    ),
                                  ],
                                ),
                                const SizedBox(height: 6),
                                Text(
                                  'Type the 6-digit numeric pairing code displayed by Pconnect on your desktop.',
                                  style: TextStyle(fontSize: 12, color: Colors.white.withOpacity(0.65)),
                                ),
                                const SizedBox(height: 16),
                                Row(
                                  children: [
                                    Expanded(
                                      child: TextField(
                                        controller: _codeController,
                                        keyboardType: TextInputType.number,
                                        maxLength: 6,
                                        style: const TextStyle(color: Colors.white, letterSpacing: 4.0, fontWeight: FontWeight.bold),
                                        decoration: InputDecoration(
                                          counterText: '',
                                          hintText: '000000',
                                          hintStyle: TextStyle(color: Colors.white.withOpacity(0.2)),
                                          border: OutlineInputBorder(
                                            borderRadius: BorderRadius.circular(12),
                                          ),
                                          filled: true,
                                          fillColor: Colors.white.withOpacity(0.04),
                                        ),
                                        onSubmitted: (_) => _submitCode(),
                                      ),
                                    ),
                                    const SizedBox(width: 12),
                                    SizedBox(
                                      height: 52,
                                      child: FilledButton(
                                        onPressed: _submitCode,
                                        style: FilledButton.styleFrom(
                                          shape: RoundedRectangleBorder(
                                            borderRadius: BorderRadius.circular(12),
                                          ),
                                        ),
                                        child: const Text('Pair'),
                                      ),
                                    ),
                                  ],
                                ),
                              ],
                            ),
                          ),
                        ),
                        const SizedBox(height: 20),
                      ],

                      // ── Connection Options (QR + Manual) ──
                      Row(
                        children: [
                          // Camera QR Scanner Card
                          Expanded(
                            child: InkWell(
                              onTap: _openQrScanner,
                              borderRadius: BorderRadius.circular(20),
                              child: Container(
                                height: 120,
                                decoration: BoxDecoration(
                                  color: Colors.white.withOpacity(0.04),
                                  borderRadius: BorderRadius.circular(20),
                                  border: Border.all(color: Colors.white.withOpacity(0.08)),
                                ),
                                child: Column(
                                  mainAxisAlignment: MainAxisAlignment.center,
                                  children: [
                                    Container(
                                      padding: const EdgeInsets.all(12),
                                      decoration: BoxDecoration(
                                        color: cs.primary.withOpacity(0.12),
                                        shape: BoxShape.circle,
                                      ),
                                      child: Icon(Icons.qr_code_scanner_rounded, color: cs.primary, size: 28),
                                    ),
                                    const SizedBox(height: 10),
                                    const Text(
                                      'Scan QR Code',
                                      style: TextStyle(
                                        color: Colors.white,
                                        fontWeight: FontWeight.w600,
                                        fontSize: 13,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(width: 12),

                          // Network discovery triggers
                          Expanded(
                            child: InkWell(
                              onTap: _scanNetwork,
                              borderRadius: BorderRadius.circular(20),
                              child: Container(
                                height: 120,
                                decoration: BoxDecoration(
                                  color: Colors.white.withOpacity(0.04),
                                  borderRadius: BorderRadius.circular(20),
                                  border: Border.all(color: Colors.white.withOpacity(0.08)),
                                ),
                                child: Column(
                                  mainAxisAlignment: MainAxisAlignment.center,
                                  children: [
                                    Container(
                                      padding: const EdgeInsets.all(12),
                                      decoration: BoxDecoration(
                                        color: cs.secondary.withOpacity(0.12),
                                        shape: BoxShape.circle,
                                      ),
                                      child: _scanning
                                          ? SizedBox(
                                              width: 28,
                                              height: 28,
                                              child: CircularProgressIndicator(strokeWidth: 2.5, color: cs.secondary),
                                            )
                                          : Icon(Icons.radar_rounded, color: cs.secondary, size: 28),
                                    ),
                                    const SizedBox(height: 10),
                                    Text(
                                      _scanning ? 'Searching...' : 'Scan Local Network',
                                      style: const TextStyle(
                                        color: Colors.white,
                                        fontWeight: FontWeight.w600,
                                        fontSize: 13,
                                      ),
                                    ),
                                  ],
                                ),
                              ),
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 24),

                      // ── Manual Connection (Glassmorphic) ──
                      const Text(
                        'MANUAL PC ADDRESS',
                        style: TextStyle(
                          color: Colors.white54,
                          fontWeight: FontWeight.bold,
                          fontSize: 11,
                          letterSpacing: 1.0,
                        ),
                      ),
                      const SizedBox(height: 8),
                      Container(
                        padding: const EdgeInsets.all(16),
                        decoration: BoxDecoration(
                          color: Colors.white.withOpacity(0.03),
                          borderRadius: BorderRadius.circular(20),
                          border: Border.all(color: Colors.white.withOpacity(0.08)),
                        ),
                        child: Column(
                          children: [
                            Row(
                              children: [
                                Expanded(
                                  flex: 3,
                                  child: TextField(
                                    controller: _ipController,
                                    keyboardType: TextInputType.text,
                                    style: const TextStyle(color: Colors.white),
                                    decoration: InputDecoration(
                                      labelText: 'PC Host / IP Address',
                                      labelStyle: const TextStyle(color: Colors.white38),
                                      hintText: '192.168.1.100',
                                      hintStyle: TextStyle(color: Colors.white.withOpacity(0.15)),
                                      border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                                      isDense: true,
                                    ),
                                  ),
                                ),
                                const SizedBox(width: 10),
                                Expanded(
                                  child: TextField(
                                    controller: _portController,
                                    keyboardType: TextInputType.number,
                                    style: const TextStyle(color: Colors.white),
                                    decoration: InputDecoration(
                                      labelText: 'Port',
                                      labelStyle: const TextStyle(color: Colors.white38),
                                      border: OutlineInputBorder(borderRadius: BorderRadius.circular(12)),
                                      isDense: true,
                                    ),
                                  ),
                                ),
                              ],
                            ),
                            const SizedBox(height: 12),
                            SizedBox(
                              width: double.infinity,
                              height: 48,
                              child: FilledButton.icon(
                                onPressed: () {
                                  final ip = _ipController.text.trim();
                                  final port = int.tryParse(_portController.text.trim()) ?? kWsPortDefault;
                                  if (ip.isNotEmpty) {
                                    _connectTo(ip, port, wssPort: kDefaultWssPort);
                                  }
                                },
                                style: FilledButton.styleFrom(
                                  shape: RoundedRectangleBorder(
                                    borderRadius: BorderRadius.circular(12),
                                  ),
                                ),
                                icon: const Icon(Icons.link_rounded),
                                label: const Text('Connect Host'),
                              ),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 24),

                      // ── Connection Diagnostics/Error ──
                      if (_connError != null) ...[
                        Container(
                          padding: const EdgeInsets.all(12),
                          decoration: BoxDecoration(
                            color: Colors.red.withOpacity(0.15),
                            borderRadius: BorderRadius.circular(14),
                            border: Border.all(color: Colors.red.withOpacity(0.35)),
                          ),
                          child: Row(
                            children: [
                              const Icon(Icons.error_outline_rounded, color: Colors.redAccent, size: 20),
                              const SizedBox(width: 10),
                              Expanded(
                                child: Text(
                                  _connError!,
                                  style: const TextStyle(color: Colors.redAccent, fontSize: 13),
                                ),
                              ),
                            ],
                          ),
                        ),
                        const SizedBox(height: 24),
                      ],

                      // ── Discovered PCs on Network ──
                      if (_discovered.isNotEmpty) ...[
                        const Text(
                          'FOUND ON LOCAL NETWORK',
                          style: TextStyle(
                            color: Colors.white54,
                            fontWeight: FontWeight.bold,
                            fontSize: 11,
                            letterSpacing: 1.0,
                          ),
                        ),
                        const SizedBox(height: 8),
                        ..._discovered.map((pc) => Card(
                          color: Colors.white.withOpacity(0.04),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(14),
                            side: BorderSide(color: Colors.white.withOpacity(0.06)),
                          ),
                          margin: const EdgeInsets.only(bottom: 8),
                          child: ListTile(
                            leading: CircleAvatar(
                              backgroundColor: cs.primary.withOpacity(0.12),
                              child: Icon(Icons.computer_rounded, color: cs.primary),
                            ),
                            title: Text(pc.name, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w600)),
                            subtitle: Text('${pc.address.address}:${pc.wsPort}', style: TextStyle(color: Colors.white.withOpacity(0.4))),
                            trailing: FilledButton.tonal(
                              onPressed: () => _connectTo(pc.address.address, pc.wsPort, wssPort: pc.wssPort),
                              style: FilledButton.styleFrom(
                                shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                              ),
                              child: const Text('Link'),
                            ),
                          ),
                        )),
                        const SizedBox(height: 20),
                      ],

                      // ── Saved Profiles ──
                      if (_profiles.isNotEmpty) ...[
                        const Text(
                          'SAVED PROFILES & HISTORY',
                          style: TextStyle(
                            color: Colors.white54,
                            fontWeight: FontWeight.bold,
                            fontSize: 11,
                            letterSpacing: 1.0,
                          ),
                        ),
                        const SizedBox(height: 8),
                        ..._profiles.map((p) => Card(
                          color: Colors.white.withOpacity(0.03),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(14),
                            side: BorderSide(color: Colors.white.withOpacity(0.05)),
                          ),
                          margin: const EdgeInsets.only(bottom: 8),
                          child: ListTile(
                            leading: CircleAvatar(
                              backgroundColor: cs.secondary.withOpacity(0.12),
                              child: Icon(Icons.bookmark_rounded, color: cs.secondary),
                            ),
                            title: Text(p.name.isEmpty ? p.ip : p.name, style: const TextStyle(color: Colors.white, fontWeight: FontWeight.w600)),
                            subtitle: Text('${p.ip}:${p.port}', style: TextStyle(color: Colors.white.withOpacity(0.4))),
                            trailing: Row(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                IconButton(
                                  icon: const Icon(Icons.delete_outline_rounded, color: Colors.redAccent, size: 20),
                                  onPressed: () async {
                                    await ProfileStore.remove(p.ip, p.port);
                                    _loadProfiles();
                                  },
                                ),
                                FilledButton.tonal(
                                  onPressed: () => _connectTo(p.ip, p.port, wssPort: p.wssPort),
                                  style: FilledButton.styleFrom(
                                    shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(8)),
                                  ),
                                  child: const Text('Link'),
                                ),
                              ],
                            ),
                          ),
                        )),
                      ],

                      const SizedBox(height: 48),
                    ]),
                  ),
                ),
              ],
            ),
          ),

          // ── Dynamic Connection Blur Progress Overlay ──
          if (_connecting)
            Positioned.fill(
              child: Container(
                color: Colors.black.withOpacity(0.6),
                child: Center(
                  child: Card(
                    color: const Color(0xFF1E293B),
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(20),
                      side: BorderSide(color: Colors.white.withOpacity(0.1)),
                    ),
                    elevation: 12,
                    child: Padding(
                      padding: const EdgeInsets.symmetric(horizontal: 40, vertical: 30),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          const CircularProgressIndicator(strokeWidth: 3.5),
                          const SizedBox(height: 20),
                          const Text(
                            'Establishing Bridge...',
                            style: TextStyle(
                              color: Colors.white,
                              fontSize: 16,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                          const SizedBox(height: 6),
                          const Text(
                            'Securing connection via websocket',
                            style: TextStyle(
                              color: Colors.white54,
                              fontSize: 12,
                            ),
                          ),
                          const SizedBox(height: 20),
                          TextButton.icon(
                            onPressed: () {
                              setState(() {
                                _connecting = false;
                              });
                              widget.onCancel?.call();
                            },
                            icon: const Icon(Icons.close_rounded),
                            label: const Text('Cancel Connection'),
                            style: TextButton.styleFrom(
                              foregroundColor: Colors.redAccent,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
        ],
      ),
    );
  }
}

// ── Mini logo custom painter ──
class _MiniLogoPainter extends CustomPainter {
  final Color accentColor;

  _MiniLogoPainter({required this.accentColor});

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final radius = size.width / 2;

    final paintBg = Paint()
      ..shader = RadialGradient(
        colors: [
          accentColor,
          const Color(0xFF3B0066),
        ],
      ).createShader(Rect.fromCircle(center: center, radius: radius))
      ..style = PaintingStyle.fill;

    canvas.drawCircle(center, radius, paintBg);

    final border = Paint()
      ..color = Colors.white.withOpacity(0.2)
      ..strokeWidth = 1.0
      ..style = PaintingStyle.stroke;
    canvas.drawCircle(center, radius - 3, border);

    final textPainter = TextPainter(
      textDirection: TextDirection.ltr,
    );

    textPainter.text = const TextSpan(
      text: 'P',
      style: TextStyle(
        fontSize: 34,
        fontFamily: 'Outfit',
        fontWeight: FontWeight.w900,
        color: Colors.white,
      ),
    );

    textPainter.layout();
    textPainter.paint(
      canvas,
      Offset(center.dx - textPainter.width / 2, center.dy - textPainter.height / 2 - 2),
    );
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}

// ── Modern styled QR scan page ──
class _ModernQrScanPage extends StatefulWidget {
  final void Function(String ip, int port, int? wssPort, String? pairingCode) onResult;

  const _ModernQrScanPage({required this.onResult});

  @override
  State<_ModernQrScanPage> createState() => _ModernQrScanPageState();
}

class _ModernQrScanPageState extends State<_ModernQrScanPage> {
  MobileScannerController? _controller;
  bool _handled = false;
  bool _permissionDenied = false;
  bool _permissionPermanentlyDenied = false;
  bool _checking = true;

  @override
  void initState() {
    super.initState();
    _requestCameraPermission();
  }

  Future<void> _requestCameraPermission() async {
    final status = await Permission.camera.request();
    if (!mounted) return;

    if (status.isGranted) {
      setState(() {
        _checking = false;
        _controller = MobileScannerController();
      });
    } else if (status.isPermanentlyDenied) {
      setState(() {
        _permissionPermanentlyDenied = true;
        _checking = false;
      });
    } else {
      setState(() {
        _permissionDenied = true;
        _checking = false;
      });
    }
  }

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  void _onDetect(BarcodeCapture capture) {
    if (_handled) return;
    for (final barcode in capture.barcodes) {
      final raw = barcode.rawValue;
      if (raw == null) continue;
      try {
        final json = jsonDecode(raw) as Map<String, dynamic>;
        final ip = json['ip'] as String?;
        final port = (json['port'] as num?)?.toInt() ?? kWsPortDefault;
        final wssPort = (json['wssPort'] as num?)?.toInt();
        final code = json['pairingCode'] as String?;
        if (ip == null || ip.isEmpty || ip == '0.0.0.0') continue;
        _handled = true;
        widget.onResult(ip, port, wssPort, code);
        return;
      } catch (_) {
        // Ignored
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      backgroundColor: const Color(0xFF0F172A),
      appBar: AppBar(
        title: const Text('Scan PC QR Code'),
        backgroundColor: Colors.transparent,
        elevation: 0,
        foregroundColor: Colors.white,
      ),
      body: _checking
          ? const Center(child: CircularProgressIndicator())
          : _permissionPermanentlyDenied
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(32),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Icon(Icons.camera_alt_outlined, size: 64, color: cs.error),
                        const SizedBox(height: 16),
                        const Text(
                          'Camera Access Blocked',
                          style: TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.bold),
                          textAlign: TextAlign.center,
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'Please enable camera permissions in application settings to scan PC QR Codes.',
                          style: TextStyle(color: Colors.white.withOpacity(0.6)),
                          textAlign: TextAlign.center,
                        ),
                        const SizedBox(height: 24),
                        FilledButton.icon(
                          onPressed: () => openAppSettings(),
                          icon: const Icon(Icons.settings),
                          label: const Text('Open App Settings'),
                        ),
                      ],
                    ),
                  ),
                )
              : _permissionDenied
                  ? Center(
                      child: Padding(
                        padding: const EdgeInsets.all(32),
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            Icon(Icons.no_photography_outlined, size: 64, color: cs.error),
                            const SizedBox(height: 16),
                            const Text(
                              'Camera Access Denied',
                              style: TextStyle(color: Colors.white, fontSize: 20, fontWeight: FontWeight.bold),
                              textAlign: TextAlign.center,
                            ),
                            const SizedBox(height: 8),
                            Text(
                              'Pconnect needs camera access to read connection QR codes.',
                              style: TextStyle(color: Colors.white.withOpacity(0.6)),
                              textAlign: TextAlign.center,
                            ),
                            const SizedBox(height: 24),
                            FilledButton.icon(
                              onPressed: _requestCameraPermission,
                              icon: const Icon(Icons.refresh),
                              label: const Text('Try Request Again'),
                            ),
                          ],
                        ),
                      ),
                    )
                  : Stack(
                      children: [
                        MobileScanner(
                          controller: _controller!,
                          onDetect: _onDetect,
                        ),
                        // Transparent targeting viewport
                        Center(
                          child: Container(
                            width: 260,
                            height: 260,
                            decoration: BoxDecoration(
                              border: Border.all(color: cs.primary, width: 3.0),
                              borderRadius: BorderRadius.circular(24),
                            ),
                          ),
                        ),
                        Positioned(
                          bottom: 32,
                          left: 20,
                          right: 20,
                          child: Center(
                            child: Card(
                              color: Colors.black87,
                              shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(12)),
                              child: Padding(
                                padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
                                child: Text(
                                  'Aim camera at the QR code displayed on the PC Agent popup window',
                                  textAlign: TextAlign.center,
                                  style: TextStyle(color: Colors.white.withOpacity(0.9), fontSize: 12),
                                ),
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
    );
  }
}
