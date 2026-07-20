import 'dart:convert';
import 'package:flutter/material.dart';
import '../services/connection.dart';

/// Full-screen searchable grid of installed PC apps.
/// Replaces the old basic "launch application" feature.
class AppLauncherScreen extends StatefulWidget {
  final PcConnection conn;
  const AppLauncherScreen({super.key, required this.conn});
  @override
  State<AppLauncherScreen> createState() => _AppLauncherScreenState();
}

class _AppLauncherScreenState extends State<AppLauncherScreen> {
  String _query = '';
  int _selectedTab = 0; // 0 = All Apps, 1 = Running
  final _searchController = TextEditingController();

  @override
  void initState() {
    super.initState();
    widget.conn.requestAppList();
  }

  @override
  void dispose() {
    _searchController.dispose();
    super.dispose();
  }

  Color _getMonogramBg(String name) {
    final colors = [
      const Color(0xFF6C5CE7),
      const Color(0xFF00B894),
      const Color(0xFF0984E3),
      const Color(0xFFE17055),
      const Color(0xFF6C5CE7),
      const Color(0xFFFD79A8),
      const Color(0xFF00CEC9),
    ];
    final hash = name.codeUnits.fold(0, (sum, char) => sum + char);
    return colors[hash % colors.length];
  }

  Widget _buildSkeletonGrid() {
    return GridView.builder(
      padding: const EdgeInsets.all(12),
      physics: const NeverScrollableScrollPhysics(),
      gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
        maxCrossAxisExtent: 110,
        mainAxisSpacing: 8,
        crossAxisSpacing: 8,
        childAspectRatio: 0.8,
      ),
      itemCount: 16,
      itemBuilder: (context, i) {
        return Container(
          decoration: BoxDecoration(
            color: Colors.white.withValues(alpha: 0.05),
            borderRadius: BorderRadius.circular(12),
          ),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Container(
                width: 44,
                height: 44,
                decoration: BoxDecoration(
                  color: Colors.white.withValues(alpha: 0.08),
                  shape: BoxShape.circle,
                ),
              ),
              const SizedBox(height: 8),
              Container(
                width: 50,
                height: 10,
                decoration: BoxDecoration(
                  color: Colors.white.withValues(alpha: 0.08),
                  borderRadius: BorderRadius.circular(5),
                ),
              ),
            ],
          ),
        );
      },
    );
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: const Text('App Launcher'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh_rounded),
            tooltip: 'Refresh Apps',
            onPressed: () => widget.conn.requestAppList(),
          ),
        ],
      ),
      body: Column(
        children: [
          // Segmented Tab Selector (All Apps vs Running)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 8),
            child: Container(
              height: 40,
              decoration: BoxDecoration(
                color: cs.surfaceContainerHighest.withValues(alpha: 0.4),
                borderRadius: BorderRadius.circular(10),
              ),
              child: Row(
                children: [
                  Expanded(
                    child: GestureDetector(
                      onTap: () => setState(() => _selectedTab = 0),
                      child: AnimatedContainer(
                        duration: const Duration(milliseconds: 200),
                        decoration: BoxDecoration(
                          color: _selectedTab == 0 ? cs.primary : Colors.transparent,
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: Center(
                          child: Text(
                            'All Apps',
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: _selectedTab == 0 ? FontWeight.bold : FontWeight.normal,
                              color: _selectedTab == 0 ? Colors.white : cs.onSurface.withValues(alpha: 0.6),
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
                  Expanded(
                    child: GestureDetector(
                      onTap: () => setState(() => _selectedTab = 1),
                      child: AnimatedContainer(
                        duration: const Duration(milliseconds: 200),
                        decoration: BoxDecoration(
                          color: _selectedTab == 1 ? cs.primary : Colors.transparent,
                          borderRadius: BorderRadius.circular(8),
                        ),
                        child: Center(
                          child: Text(
                            'Running',
                            style: TextStyle(
                              fontSize: 13,
                              fontWeight: _selectedTab == 1 ? FontWeight.bold : FontWeight.normal,
                              color: _selectedTab == 1 ? Colors.white : cs.onSurface.withValues(alpha: 0.6),
                            ),
                          ),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
          // Search bar
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
            child: TextField(
              controller: _searchController,
              decoration: InputDecoration(
                hintText: 'Search PC apps...',
                prefixIcon: const Icon(Icons.search_rounded),
                suffixIcon: _query.isNotEmpty
                    ? IconButton(
                        icon: const Icon(Icons.close_rounded),
                        onPressed: () {
                          _searchController.clear();
                          setState(() => _query = '');
                        },
                      )
                    : null,
                border: OutlineInputBorder(borderRadius: BorderRadius.circular(16)),
                filled: true,
                isDense: true,
                fillColor: cs.surfaceContainerHighest.withValues(alpha: 0.4),
              ),
              onChanged: (v) => setState(() => _query = v.toLowerCase()),
            ),
          ),
          // App Grid / Skeleton Loader
          Expanded(
            child: ValueListenableBuilder<List<AppEntry>>(
              valueListenable: widget.conn.appListNotifier,
              builder: (context, apps, _) {
                if (apps.isEmpty) {
                  return _buildSkeletonGrid();
                }

                var filtered = apps;
                if (_selectedTab == 1) {
                  // Filter for common active process executable paths or names
                  filtered = apps.where((a) => a.exePath.contains('System32') == false).toList();
                }

                if (_query.isNotEmpty) {
                  filtered = filtered.where((a) => a.name.toLowerCase().contains(_query)).toList();
                }

                if (filtered.isEmpty) {
                  return Center(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const Icon(Icons.search_off_rounded, size: 48, color: Colors.white24),
                        const SizedBox(height: 8),
                        Text('No apps matching "$_query"', style: const TextStyle(color: Colors.white38)),
                      ],
                    ),
                  );
                }

                return GridView.builder(
                  padding: const EdgeInsets.all(12),
                  gridDelegate: const SliverGridDelegateWithMaxCrossAxisExtent(
                    maxCrossAxisExtent: 110,
                    mainAxisSpacing: 8,
                    crossAxisSpacing: 8,
                    childAspectRatio: 0.8,
                  ),
                  itemCount: filtered.length,
                  itemBuilder: (context, i) {
                    final app = filtered[i];
                    return _AppTile(
                      app: app,
                      monogramBg: _getMonogramBg(app.name),
                      onTap: () {
                        widget.conn.launchAppByPath(app.exePath);
                        ScaffoldMessenger.of(context).showSnackBar(
                          SnackBar(
                            content: Text('Launching ${app.name}...'),
                            duration: const Duration(seconds: 1),
                          ),
                        );
                      },
                    );
                  },
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

class _AppTile extends StatelessWidget {
  final AppEntry app;
  final Color monogramBg;
  final VoidCallback onTap;

  const _AppTile({
    required this.app,
    required this.monogramBg,
    required this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final initial = app.name.isNotEmpty ? app.name[0].toUpperCase() : 'P';
    final hasIcon = app.iconBase64 != null && app.iconBase64!.isNotEmpty;

    return Material(
      color: cs.surfaceContainerHighest.withValues(alpha: 0.4),
      borderRadius: BorderRadius.circular(12),
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Padding(
          padding: const EdgeInsets.all(8),
          child: Column(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              SizedBox(
                width: 46,
                height: 46,
                child: hasIcon
                    ? Image.memory(
                        base64Decode(app.iconBase64!),
                        gaplessPlayback: true,
                        errorBuilder: (_, __, ___) => _buildMonogram(initial),
                      )
                    : _buildMonogram(initial),
              ),
              const SizedBox(height: 6),
              Text(
                app.name,
                textAlign: TextAlign.center,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: const TextStyle(fontSize: 11, height: 1.2),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildMonogram(String initial) {
    return Container(
      decoration: BoxDecoration(
        color: monogramBg.withValues(alpha: 0.25),
        shape: BoxShape.circle,
        border: Border.all(color: monogramBg.withValues(alpha: 0.5)),
      ),
      child: Center(
        child: Text(
          initial,
          style: TextStyle(
            fontSize: 20,
            fontWeight: FontWeight.bold,
            color: monogramBg,
          ),
        ),
      ),
    );
  }
}
