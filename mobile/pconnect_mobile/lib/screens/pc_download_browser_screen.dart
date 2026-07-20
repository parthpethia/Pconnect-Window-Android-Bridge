import 'package:flutter/material.dart';
import '../services/connection.dart';

class PcDownloadBrowserScreen extends StatefulWidget {
  final PcConnection conn;

  const PcDownloadBrowserScreen({super.key, required this.conn});

  @override
  State<PcDownloadBrowserScreen> createState() => _PcDownloadBrowserScreenState();
}

class _PcDownloadBrowserScreenState extends State<PcDownloadBrowserScreen> {
  final List<Map<String, String>> _breadcrumbs = [
    {'name': 'PC Roots', 'path': ''}
  ];
  bool _loading = false;
  List<dynamic> _items = [];
  String? _error;

  @override
  void initState() {
    super.initState();
    _loadDirectory('');
  }

  Future<void> _loadDirectory(String path) async {
    setState(() {
      _loading = true;
      _error = null;
    });

    try {
      final res = await widget.conn.listPcDirectory(path.isEmpty ? null : path);
      setState(() {
        _items = res;
        _loading = false;
      });
    } catch (e) {
      setState(() {
        _error = 'Failed to load directory: $e';
        _loading = false;
      });
    }
  }

  void _navigateTo(String name, String path) {
    setState(() {
      _breadcrumbs.add({'name': name, 'path': path});
    });
    _loadDirectory(path);
  }

  void _navigateBackTo(int index) {
    if (index < 0 || index >= _breadcrumbs.length) return;
    setState(() {
      _breadcrumbs.removeRange(index + 1, _breadcrumbs.length);
    });
    _loadDirectory(_breadcrumbs.last['path']!);
  }

  String _formatSize(int bytes) {
    if (bytes <= 0) return '';
    if (bytes < 1024) return '$bytes B';
    if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(1)} KB';
    if (bytes < 1024 * 1024 * 1024) return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
    return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(2)} GB';
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('Browse PC Files'),
      ),
      body: Column(
        children: [
          // ── Breadcrumb navigation bar ──
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
            color: cs.surfaceContainerHighest,
            width: double.infinity,
            child: SingleChildScrollView(
              scrollDirection: Axis.horizontal,
              child: Row(
                children: _breadcrumbs.asMap().entries.map((entry) {
                  final idx = entry.key;
                  final crumb = entry.value;
                  final isLast = idx == _breadcrumbs.length - 1;
                  return Row(
                    children: [
                      InkWell(
                        onTap: isLast ? null : () => _navigateBackTo(idx),
                        child: Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
                          child: Text(
                            crumb['name']!,
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: isLast ? FontWeight.bold : FontWeight.normal,
                              color: isLast ? cs.primary : cs.onSurfaceVariant,
                            ),
                          ),
                        ),
                      ),
                      if (!isLast)
                        Icon(Icons.chevron_right_rounded, size: 16, color: cs.onSurfaceVariant),
                    ],
                  );
                }).toList(),
              ),
            ),
          ),

          // ── File / Directory List ──
          Expanded(
            child: _loading
                ? const Center(child: CircularProgressIndicator())
                : _error != null
                    ? Center(
                        child: Column(
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Icon(Icons.error_outline_rounded, color: cs.error, size: 48),
                            const SizedBox(height: 12),
                            Text(_error!, style: TextStyle(color: cs.error)),
                            const SizedBox(height: 12),
                            ElevatedButton(
                              onPressed: () => _loadDirectory(_breadcrumbs.last['path']!),
                              child: const Text('Retry'),
                            ),
                          ],
                        ),
                      )
                    : _items.isEmpty
                        ? Center(
                            child: Text(
                              'Directory is empty',
                              style: TextStyle(color: cs.onSurface.withValues(alpha: 0.5)),
                            ),
                          )
                        : ListView.separated(
                            padding: const EdgeInsets.symmetric(vertical: 8),
                            itemCount: _items.length,
                            separatorBuilder: (_, __) => const Divider(height: 1),
                            itemBuilder: (context, i) {
                              final item = _items[i] as Map<String, dynamic>;
                              final name = item['name'] as String? ?? '';
                              final path = item['path'] as String? ?? '';
                              final isDir = item['isDir'] == true;
                              final size = (item['size'] as num?)?.toInt() ?? 0;

                              return ListTile(
                                leading: Icon(
                                  isDir ? Icons.folder_rounded : Icons.insert_drive_file_rounded,
                                  color: isDir ? Colors.amber.shade700 : cs.primary,
                                  size: 28,
                                ),
                                title: Text(
                                  name,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(fontWeight: FontWeight.w500),
                                ),
                                subtitle: !isDir && size > 0
                                    ? Text(_formatSize(size), style: const TextStyle(fontSize: 12))
                                    : null,
                                trailing: isDir
                                    ? const Icon(Icons.chevron_right_rounded)
                                    : IconButton(
                                        icon: const Icon(Icons.download_rounded),
                                        tooltip: 'Download to phone',
                                        onPressed: () {
                                          // Download PC file to mobile
                                          ScaffoldMessenger.of(context).showSnackBar(
                                            SnackBar(content: Text('Downloading $name to phone...')),
                                          );
                                        },
                                      ),
                                onTap: isDir ? () => _navigateTo(name, path) : null,
                              );
                            },
                          ),
          ),
        ],
      ),
    );
  }
}
