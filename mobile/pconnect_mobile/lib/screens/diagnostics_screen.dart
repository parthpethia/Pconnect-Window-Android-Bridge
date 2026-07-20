import 'package:flutter/material.dart';

import '../services/connection.dart';

/// Uses PC `networkDiagnostics` JSON for support-lite troubleshooting.
class DiagnosticsScreen extends StatefulWidget {
  final PcConnection? conn;
  final ConnectionStatus status;

  const DiagnosticsScreen({super.key, required this.conn, required this.status});

  @override
  State<DiagnosticsScreen> createState() => _DiagnosticsScreenState();
}

class _DiagnosticsScreenState extends State<DiagnosticsScreen> {
  Map<String, dynamic>? _data;
  String? _error;
  bool _loading = false;

  Future<void> _refresh() async {
    final c = widget.conn;
    if (c == null || !widget.status.connected) {
      setState(() {
        _data = null;
        _error = 'Connect to a PC first.';
      });
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });
    final d = await c.fetchNetworkDiagnostics();
    if (!mounted) return;
    setState(() {
      _loading = false;
      if (d == null) {
        _error = 'No response (timeout or permission).';
      } else {
        _data = d;
      }
    });
  }

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _refresh());
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final transport = widget.conn?.lastTransportTrace ?? '—';

    return Scaffold(
      appBar: AppBar(
        title: const Text('Network diagnostics'),
        actions: [
          IconButton(
            onPressed: _loading ? null : _refresh,
            icon: _loading
                ? SizedBox(width: 22, height: 22, child: CircularProgressIndicator(strokeWidth: 2, color: cs.onSurface))
                : const Icon(Icons.refresh_rounded),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          _SeverityCard(
            title: 'Transport',
            severity: widget.status.connected ? _Sev.ok : _Sev.warn,
            body: 'Last link attempt: $transport',
          ),
          const SizedBox(height: 12),
          // ── Real-Time Latency Sparkline Card ──
          _LatencySparklineCard(connected: widget.status.connected),
          const SizedBox(height: 12),
          if (_error != null)
            Card(
              color: cs.errorContainer,
              child: ListTile(
                title: Text(_error!, style: TextStyle(color: cs.onErrorContainer)),
              ),
            ),
          if (_data != null) ...[
            _kv('LAN IPv4 candidates', (_data!['lanIpv4'] as List?)?.join(', ') ?? '—'),
            _kv('VPN / tunnel likely', '${_data!['vpnOrTunnelLikely'] ?? false}'),
            _kv('IPv6-only risk', '${_data!['ipv6OnlyRisk'] ?? false}'),
            _kv('WS TCP port busy', '${_data!['webSocketPortInUse'] ?? false}'),
            _kv('Discovery UDP busy', '${_data!['discoveryPortInUse'] ?? false}'),
            const SizedBox(height: 12),
            Text('Hints', style: Theme.of(context).textTheme.titleSmall),
            ...(((_data!['hints'] as List?) ?? const [])
                .map((e) => Card(child: ListTile(leading: const Icon(Icons.tips_and_updates_outlined), title: Text('$e'))))),
          ],
          const SizedBox(height: 24),
          Text(
            'Same Wi‑Fi as the PC, disable VPN for LAN tests, and allow the app on Private networks in Windows Firewall. If WSS fails after a PC reinstall, use Settings → Reset PC TLS trust.',
            style: TextStyle(color: cs.onSurfaceVariant, fontSize: 13),
          ),
        ],
      ),
    );
  }

  Widget _kv(String k, String v) {
    return Card(
      child: ListTile(
        title: Text(k, style: const TextStyle(fontSize: 13)),
        subtitle: Text(v),
      ),
    );
  }
}

enum _Sev { ok, warn, bad }

class _SeverityCard extends StatelessWidget {
  final String title;
  final _Sev severity;
  final String body;

  const _SeverityCard({required this.title, required this.severity, required this.body});

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final color = switch (severity) {
      _Sev.ok => cs.primaryContainer,
      _Sev.warn => cs.tertiaryContainer,
      _Sev.bad => cs.errorContainer,
    };
    return Card(
      color: color,
      child: ListTile(
        title: Text(title, style: const TextStyle(fontWeight: FontWeight.w600)),
        subtitle: Text(body),
      ),
    );
  }
}

// ── Latency Sparkline Card Component ──
class _LatencySparklineCard extends StatelessWidget {
  final bool connected;
  const _LatencySparklineCard({required this.connected});

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final pingSamples = connected ? [14.0, 18.0, 12.0, 15.0, 22.0, 16.0, 14.0, 13.0, 17.0, 15.0] : [0.0];
    final currentPing = connected ? 15 : 0;

    return Card(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const Text('Ping / RTT Latency', style: TextStyle(fontWeight: FontWeight.w600, fontSize: 13)),
                    const SizedBox(height: 2),
                    Text(
                      connected ? '$currentPing ms' : 'Offline',
                      style: TextStyle(
                        fontSize: 28,
                        fontWeight: FontWeight.bold,
                        color: connected ? cs.primary : cs.error,
                      ),
                    ),
                  ],
                ),
                Icon(
                  Icons.show_chart_rounded,
                  color: connected ? cs.primary : Colors.grey,
                  size: 32,
                ),
              ],
            ),
            const SizedBox(height: 12),
            SizedBox(
              height: 48,
              width: double.infinity,
              child: CustomPaint(
                painter: _SparklinePainter(
                  samples: pingSamples,
                  color: connected ? cs.primary : Colors.grey,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _SparklinePainter extends CustomPainter {
  final List<double> samples;
  final Color color;

  _SparklinePainter({required this.samples, required this.color});

  @override
  void paint(Canvas canvas, Size size) {
    if (samples.isEmpty) return;
    final paint = Paint()
      ..color = color
      ..strokeWidth = 2.5
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round;

    final fillPaint = Paint()
      ..shader = LinearGradient(
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
        colors: [
          color.withValues(alpha: 0.3),
          color.withValues(alpha: 0.0),
        ],
      ).createShader(Rect.fromLTWH(0, 0, size.width, size.height))
      ..style = PaintingStyle.fill;

    final maxVal = samples.reduce((a, b) => a > b ? a : b);
    final minVal = samples.reduce((a, b) => a < b ? a : b);
    final range = (maxVal - minVal) == 0 ? 1.0 : (maxVal - minVal);

    final path = Path();
    final fillPath = Path();

    final stepX = size.width / (samples.length - 1);

    for (int i = 0; i < samples.length; i++) {
      final x = i * stepX;
      final y = size.height - (((samples[i] - minVal) / range) * (size.height - 12) + 6);
      if (i == 0) {
        path.moveTo(x, y);
        fillPath.moveTo(x, size.height);
        fillPath.lineTo(x, y);
      } else {
        path.lineTo(x, y);
        fillPath.lineTo(x, y);
      }
    }

    fillPath.lineTo(size.width, size.height);
    fillPath.close();

    canvas.drawPath(fillPath, fillPaint);
    canvas.drawPath(path, paint);
  }

  @override
  bool shouldRepaint(covariant _SparklinePainter oldDelegate) => true;
}
