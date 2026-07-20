import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:file_picker/file_picker.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:flutter_webrtc/flutter_webrtc.dart';
import 'app_launcher_screen.dart';

import '../constants/theme_tokens.dart';
import '../widgets/glass_card.dart';
import '../widgets/collapsible_section.dart';
import '../services/connection.dart';
import '../main.dart';
import '../widgets/screen_preview_webrtc.dart';
import '../widgets/transfer_progress_sheet.dart';
import '../widgets/resume_transfer_dialog.dart';
import 'transfer_queue_screen.dart';
import 'pc_download_browser_screen.dart';

class HomeScreen extends StatefulWidget {
  final PcConnection? conn;
  final ConnectionStatus status;
  final VoidCallback onOpenDiscovery;

  const HomeScreen({
    super.key,
    required this.conn,
    required this.status,
    required this.onOpenDiscovery,
  });

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  double _volume = 50;
  bool _screenPreviewOn = false;
  Timer? _volumeDebounce;

  @override
  void initState() {
    super.initState();
    // Request app list + commands on connect
    if (widget.status.connected && widget.conn != null) {
      widget.conn?.requestAppList();
      widget.conn?.requestCommands();
      WidgetsBinding.instance.addPostFrameCallback((_) {
        ResumeTransferDialog.checkAndShow(context, widget.conn!);
      });
    }
  }

  @override
  void didUpdateWidget(HomeScreen old) {
    super.didUpdateWidget(old);
    if (widget.status.connected && !old.status.connected && widget.conn != null) {
      widget.conn?.requestAppList();
      widget.conn?.requestCommands();
      WidgetsBinding.instance.addPostFrameCallback((_) {
        ResumeTransferDialog.checkAndShow(context, widget.conn!);
      });
    }
  }

  @override
  void dispose() {
    _volumeDebounce?.cancel();
    super.dispose();
  }

  void _sendVolume(double v) {
    _volumeDebounce?.cancel();
    _volumeDebounce = Timer(const Duration(milliseconds: 60), () {
      widget.conn?.setVolume(level: v.round());
    });
  }

  Future<void> _pickAndUploadFiles(BuildContext context) async {
    final conn = widget.conn;
    if (conn == null) return;

    final mode = await showModalBottomSheet<String>(
      context: context,
      builder: (ctx) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              leading: const Icon(Icons.description_rounded),
              title: const Text('Select Files (Multi-Select)'),
              onTap: () => Navigator.of(ctx).pop('files'),
            ),
            ListTile(
              leading: const Icon(Icons.folder_open_rounded),
              title: const Text('Select Folder'),
              onTap: () => Navigator.of(ctx).pop('folder'),
            ),
          ],
        ),
      ),
    );

    if (!context.mounted || mode == null) return;

    if (mode == 'files') {
      final result = await FilePicker.pickFiles(allowMultiple: true);
      if (result != null && result.files.isNotEmpty) {
        final validFiles = result.files.where((f) => f.path != null).toList();
        final totalBytes = validFiles.fold<int>(0, (sum, f) => sum + f.size);

        if (validFiles.length > 50 || totalBytes > 500 * 1024 * 1024) {
          if (!context.mounted) return;
          final confirm = await showDialog<bool>(
            context: context,
            builder: (ctx) => AlertDialog(
              title: const Text('Confirm Large Batch Transfer'),
              content: Text('Selected ${validFiles.length} files (${(totalBytes / (1024 * 1024)).toStringAsFixed(1)} MB). Send all files to PC?'),
              actions: [
                TextButton(onPressed: () => Navigator.of(ctx).pop(false), child: const Text('Cancel')),
                FilledButton(onPressed: () => Navigator.of(ctx).pop(true), child: const Text('Send All')),
              ],
            ),
          );
          if (confirm != true) return;
        }

        for (final f in validFiles) {
          conn.uploadFile(f.path!, onProgress: (_) {});
        }
      }
    } else if (mode == 'folder') {
      final folderPath = await FilePicker.getDirectoryPath();
      if (folderPath != null) {
        final dir = Directory(folderPath);
        if (await dir.exists()) {
          final fileEntities = await dir.list(recursive: true).where((e) => e is File).cast<File>().toList();
          int totalBytes = 0;
          final validPaths = <String>[];

          for (final f in fileEntities) {
            final len = await f.length();
            totalBytes += len;
            validPaths.add(f.path);
          }

          if (validPaths.length > 50 || totalBytes > 500 * 1024 * 1024) {
            if (!context.mounted) return;
            final confirm = await showDialog<bool>(
              context: context,
              builder: (ctx) => AlertDialog(
                title: const Text('Confirm Folder Transfer'),
                content: Text('This folder contains ${validPaths.length} files (${(totalBytes / (1024 * 1024)).toStringAsFixed(1)} MB). Send all?'),
                actions: [
                  TextButton(onPressed: () => Navigator.of(ctx).pop(false), child: const Text('Cancel')),
                  FilledButton(onPressed: () => Navigator.of(ctx).pop(true), child: const Text('Send All')),
                ],
              ),
            );
            if (confirm != true) return;
          }

          for (final p in validPaths) {
            conn.uploadFile(p, onProgress: (_) {});
          }
        }
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final connected = widget.status.connected;
    final conn = widget.conn;

    final content = Scaffold(
      appBar: AppBar(
        title: Text(widget.status.pcName ?? 'Pconnect'),
        actions: [
          if (connected && conn != null)
            IconButton(
              icon: const Icon(Icons.swap_vert_circle_outlined),
              tooltip: 'Transfers Queue',
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute(builder: (_) => TransferQueueScreen(conn: conn, onPickFiles: () => _pickAndUploadFiles(context))),
              ),
            ),
          if (connected && conn != null)
            IconButton(
              icon: const Icon(Icons.folder_shared_outlined),
              tooltip: 'Browse PC Files',
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute(builder: (_) => PcDownloadBrowserScreen(conn: conn)),
              ),
            ),
          IconButton(
            icon: Icon(connected ? Icons.link_off_rounded : Icons.link_rounded),
            tooltip: connected ? 'Disconnect' : 'Connect',
            onPressed: widget.onOpenDiscovery,
          ),
          IconButton(
            icon: Icon(Theme.of(context).brightness == Brightness.dark
                ? Icons.light_mode : Icons.dark_mode),
            onPressed: () => ThemeControllerScope.of(context).toggle(),
          ),
        ],
      ),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          // ── Connection status bar ──
          _StatusBar(status: widget.status, onTap: widget.onOpenDiscovery),
          const SizedBox(height: 12),

          // ── Quick actions 2x2 ──
          CollapsibleSection(
            title: 'Quick Actions',
            icon: Icons.flash_on_rounded,
            storageKey: 'home_quick_actions',
            child: GridView.count(
              crossAxisCount: 2,
              shrinkWrap: true,
              physics: const NeverScrollableScrollPhysics(),
              mainAxisSpacing: 8,
              crossAxisSpacing: 8,
              childAspectRatio: 2.4,
              children: [
                _QuickAction(
                  icon: Icons.lock_rounded,
                  label: 'Lock PC',
                  enabled: connected,
                  tintColor: AppColors.danger,
                  onTap: () => conn?.lockPc(),
                ),
                _QuickAction(
                  icon: Icons.content_paste_rounded,
                  label: 'Clipboard',
                  enabled: connected,
                  tintColor: AppColors.info,
                  onTap: () async {
                    final data = await Clipboard.getData('text/plain');
                    if (data?.text != null && data!.text!.isNotEmpty) {
                      conn?.setClipboard(text: data.text!);
                      if (context.mounted) {
                        ScaffoldMessenger.of(context).showSnackBar(
                          const SnackBar(content: Text('Sent to PC clipboard')),
                        );
                      }
                    }
                  },
                ),
                _QuickAction(
                  icon: Icons.volume_off_rounded,
                  label: 'Mute',
                  enabled: connected,
                  tintColor: AppColors.warning,
                  onTap: () => conn?.mediaKey('mute'),
                ),
                _QuickAction(
                  icon: Icons.upload_file_rounded,
                  label: 'Send Files',
                  enabled: connected,
                  tintColor: AppColors.primary,
                  onTap: () => _pickAndUploadFiles(context),
                ),
              ],
            ),
          ),

          // ── Media bar ──
          CollapsibleSection(
            title: 'Media Controls',
            icon: Icons.play_circle_outline_rounded,
            storageKey: 'home_media',
            child: Column(
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceEvenly,
                  children: [
                    Container(
                      decoration: BoxDecoration(
                        color: AppColors.primary.withValues(alpha: 0.12),
                        shape: BoxShape.circle,
                        border: Border.all(color: AppColors.borderSubtle),
                      ),
                      child: IconButton(
                        onPressed: connected ? () => conn?.mediaKey('prev') : null,
                        icon: Icon(Icons.skip_previous_rounded, color: connected ? AppColors.textPrimary : AppColors.textDisabled),
                      ),
                    ),
                    Container(
                      width: 56,
                      height: 56,
                      decoration: BoxDecoration(
                        gradient: LinearGradient(
                          colors: [
                            AppColors.primary,
                            const Color(0xFF8E2DE2),
                          ],
                        ),
                        shape: BoxShape.circle,
                        boxShadow: [
                          BoxShadow(
                            color: AppColors.primaryGlow,
                            blurRadius: 16,
                          ),
                        ],
                      ),
                      child: IconButton(
                        onPressed: connected ? () => conn?.mediaKey('play_pause') : null,
                        icon: const Icon(Icons.play_arrow_rounded, color: Colors.white),
                        iconSize: 32,
                      ),
                    ),
                    Container(
                      decoration: BoxDecoration(
                        color: AppColors.primary.withValues(alpha: 0.12),
                        shape: BoxShape.circle,
                        border: Border.all(color: AppColors.borderSubtle),
                      ),
                      child: IconButton(
                        onPressed: connected ? () => conn?.mediaKey('next') : null,
                        icon: Icon(Icons.skip_next_rounded, color: connected ? AppColors.textPrimary : AppColors.textDisabled),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 12),
                Row(
                  children: [
                    const Icon(Icons.volume_down_rounded, size: 20),
                    Expanded(
                      child: Slider(
                        value: _volume,
                        min: 0,
                        max: 100,
                        activeColor: AppColors.primary,
                        onChanged: connected
                            ? (v) {
                                setState(() => _volume = v);
                                _sendVolume(v);
                              }
                            : null,
                      ),
                    ),
                    const Icon(Icons.volume_up_rounded, size: 20),
                    const SizedBox(width: 6),
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                      decoration: BoxDecoration(
                        color: AppColors.primary.withValues(alpha: 0.15),
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Text(
                        '${_volume.round()}%',
                        style: TextStyle(
                          fontSize: 11,
                          fontWeight: FontWeight.bold,
                          color: AppColors.primary,
                        ),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),

          // ── Screen preview + Trackpad ──
          CollapsibleSection(
            title: 'Screen Preview & Trackpad',
            icon: Icons.touch_app_rounded,
            storageKey: 'home_trackpad',
            child: _ScreenPreviewWithTrackpad(
              conn: conn,
              connected: connected,
              screenPreviewOn: _screenPreviewOn,
              onToggle: (v) {
                setState(() => _screenPreviewOn = v);
                if (v) {
                  conn?.startScreenCapture(intervalMs: 1000, width: 720, quality: 70);
                } else {
                  conn?.stopScreenCapture();
                }
              },
            ),
          ),

          // ── Pinned Apps row ──
          if (connected && conn != null)
            ValueListenableBuilder<List<AppEntry>>(
              valueListenable: conn.appListNotifier,
              builder: (context, apps, _) {
                if (apps.isEmpty) return const SizedBox.shrink();
                return CollapsibleSection(
                  title: 'Windows Apps',
                  icon: Icons.apps_rounded,
                  storageKey: 'home_apps',
                  trailing: TextButton(
                    onPressed: () => Navigator.of(context).push(
                      MaterialPageRoute(builder: (_) => AppLauncherScreen(conn: conn)),
                    ),
                    child: Text('View All (${apps.length})', style: AppTypography.label),
                  ),
                  child: SizedBox(
                    height: 84,
                    child: ListView.builder(
                      scrollDirection: Axis.horizontal,
                      itemCount: apps.length > 20 ? 20 : apps.length,
                      itemBuilder: (context, i) {
                        final app = apps[i];
                        return Padding(
                          padding: const EdgeInsets.only(right: 14),
                          child: GestureDetector(
                            onTap: () => conn.launchAppByPath(app.exePath),
                            child: Column(
                              children: [
                                CircleAvatar(
                                  radius: 24,
                                  backgroundColor: AppColors.primaryTint,
                                  backgroundImage: app.iconBase64 != null
                                      ? MemoryImage(base64Decode(app.iconBase64!))
                                      : null,
                                  child: app.iconBase64 == null
                                      ? const Icon(Icons.apps_rounded, color: AppColors.primary)
                                      : null,
                                ),
                                const SizedBox(height: 6),
                                SizedBox(
                                  width: 58,
                                  child: Text(
                                    app.name,
                                    textAlign: TextAlign.center,
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: AppTypography.caption,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        );
                      },
                    ),
                  ),
                );
              },
            ),
        ],
      ),
    );

    if (conn != null) {
      return TransferProgressOverlay(conn: conn, child: content);
    }
    return content;
  }
}

// ── Breathing Glow Dot ──
class _BreathingGlowDot extends StatefulWidget {
  final bool active;
  final Color color;
  const _BreathingGlowDot({required this.active, required this.color});

  @override
  State<_BreathingGlowDot> createState() => _BreathingGlowDotState();
}

class _BreathingGlowDotState extends State<_BreathingGlowDot>
    with SingleTickerProviderStateMixin {
  late AnimationController _ctrl;
  late Animation<double> _pulse;

  @override
  void initState() {
    super.initState();
    _ctrl = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1400),
    );
    _pulse = Tween<double>(begin: 1.0, end: 1.8).animate(
      CurvedAnimation(parent: _ctrl, curve: Curves.easeInOut),
    );
    if (widget.active) _ctrl.repeat(reverse: true);
  }

  @override
  void didUpdateWidget(_BreathingGlowDot oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.active != oldWidget.active) {
      if (widget.active) {
        _ctrl.repeat(reverse: true);
      } else {
        _ctrl.stop();
        _ctrl.reset();
      }
    }
  }

  @override
  void dispose() {
    _ctrl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _pulse,
      builder: (context, _) {
        final scale = widget.active ? _pulse.value : 1.0;
        return Stack(
          alignment: Alignment.center,
          children: [
            Container(
              width: 14 * scale,
              height: 14 * scale,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: widget.color.withValues(alpha: widget.active ? 0.35 / scale : 0.0),
              ),
            ),
            Container(
              width: 10,
              height: 10,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: widget.color,
              ),
            ),
          ],
        );
      },
    );
  }
}

// ── Status bar widget ──
class _StatusBar extends StatelessWidget {
  final ConnectionStatus status;
  final VoidCallback onTap;
  const _StatusBar({required this.status, required this.onTap});

  @override
  Widget build(BuildContext context) {
    final connected = status.connected;
    final statusColor = connected ? AppColors.success : AppColors.danger;
    final bgTint = connected ? AppColors.successTint : AppColors.dangerTint;

    return GlassCard(
      onTap: onTap,
      backgroundColor: bgTint,
      borderColor: statusColor.withValues(alpha: 0.3),
      child: Row(
        children: [
          _BreathingGlowDot(active: connected, color: statusColor),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  connected ? 'Connected' : 'Disconnected',
                  style: AppTypography.title.copyWith(
                    fontSize: 15,
                    color: AppColors.textPrimary,
                  ),
                ),
                if (status.pcName != null)
                  Text(
                    status.pcName!,
                    style: AppTypography.caption.copyWith(color: AppColors.textSecondary),
                  ),
                if (status.role != null)
                  Text(
                    'Role: ${status.role}',
                    style: AppTypography.caption.copyWith(color: AppColors.textDisabled),
                  ),
                if (status.error != null)
                  Text(
                    status.error!,
                    style: AppTypography.caption.copyWith(color: AppColors.danger),
                  ),
              ],
            ),
          ),
          Icon(Icons.chevron_right_rounded, color: AppColors.textSecondary),
        ],
      ),
    );
  }
}

// ── Quick action button ──
class _QuickAction extends StatelessWidget {
  final IconData icon;
  final String label;
  final bool enabled;
  final Color tintColor;
  final VoidCallback? onTap;

  const _QuickAction({
    required this.icon,
    required this.label,
    required this.enabled,
    this.tintColor = AppColors.primary,
    this.onTap,
  });

  @override
  Widget build(BuildContext context) {
    return GlassCard(
      onTap: enabled ? onTap : null,
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
      backgroundColor: enabled ? tintColor.withValues(alpha: 0.16) : AppColors.bgElevated1,
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Container(
            padding: const EdgeInsets.all(6),
            decoration: BoxDecoration(
              color: enabled ? tintColor.withValues(alpha: 0.25) : Colors.transparent,
              shape: BoxShape.circle,
            ),
            child: Icon(icon, size: 18, color: enabled ? tintColor : AppColors.textDisabled),
          ),
          const SizedBox(width: 8),
          Flexible(
            child: Text(
              label,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: AppTypography.label.copyWith(
                color: enabled ? AppColors.textPrimary : AppColors.textDisabled,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

// ── Screen Preview + Trackpad ──
class _ScreenPreviewWithTrackpad extends StatefulWidget {
  final PcConnection? conn;
  final bool connected;
  final bool screenPreviewOn;
  final ValueChanged<bool> onToggle;

  const _ScreenPreviewWithTrackpad({
    required this.conn,
    required this.connected,
    required this.screenPreviewOn,
    required this.onToggle,
  });

  @override
  State<_ScreenPreviewWithTrackpad> createState() => _ScreenPreviewWithTrackpadState();
}

class _ScreenPreviewWithTrackpadState extends State<_ScreenPreviewWithTrackpad> {
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

  Widget _buildPreview() {
    final cs = Theme.of(context).colorScheme;
    final conn = widget.conn;

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Row(
          children: [
            Text('Screen Preview', style: Theme.of(context).textTheme.titleMedium),
            const Spacer(),
            Switch(
              value: widget.screenPreviewOn,
              onChanged: widget.connected ? widget.onToggle : null,
            ),
          ],
        ),
        if (widget.screenPreviewOn && widget.connected && conn != null)
          ClipRRect(
            borderRadius: BorderRadius.circular(12),
            child: InteractiveViewer(
              minScale: 1.0,
              maxScale: 6.0,
              child: ValueListenableBuilder<RTCVideoRenderer?>(
                valueListenable: conn.webrtcRendererNotifier,
                builder: (context, renderer, _) {
                  if (renderer != null) {
                    return ValueListenableBuilder<RTCVideoValue>(
                      valueListenable: renderer,
                      builder: (context, value, _) {
                        final aspect = value.aspectRatio > 0 ? value.aspectRatio : 16 / 9;
                        return AspectRatio(
                          aspectRatio: aspect,
                          child: ScreenPreviewWebRtc(renderer: renderer),
                        );
                      },
                    );
                  }
                  return ValueListenableBuilder<Uint8List?>(
                    valueListenable: conn.screenFrameNotifier,
                    builder: (context, frame, _) {
                      if (frame == null) {
                        return Container(
                          height: 120,
                          decoration: BoxDecoration(
                            color: cs.surfaceContainerHighest,
                            borderRadius: BorderRadius.circular(12),
                          ),
                          child: const Center(child: CircularProgressIndicator()),
                        );
                      }
                      return Image.memory(
                        frame,
                        gaplessPlayback: true,
                        filterQuality: FilterQuality.medium,
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
          ),
      ],
    );
  }

  Widget _buildTrackpad() {
    final cs = Theme.of(context).colorScheme;
    final enabled = widget.connected;

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Row(
          children: [
            Text('Trackpad', style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(width: 8),
            Icon(Icons.touch_app_rounded, size: 16, color: cs.primary),
          ],
        ),
        const SizedBox(height: 6),
        AspectRatio(
          aspectRatio: 1.0,
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
              decoration: BoxDecoration(
                color: cs.surfaceContainerHighest,
                borderRadius: BorderRadius.circular(16),
                border: Border.all(color: cs.outlineVariant.withValues(alpha: 0.4)),
              ),
              child: Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Icon(Icons.touch_app, size: 36, color: cs.onSurface.withValues(alpha: 0.15)),
                    const SizedBox(height: 4),
                    Text(
                      'Tap · 2-finger right click\nLong press drag · 2-finger scroll',
                      textAlign: TextAlign.center,
                      style: TextStyle(fontSize: 10, color: cs.onSurface.withValues(alpha: 0.15)),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
        const SizedBox(height: 6),
        // Mouse buttons
        Row(
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
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    if (!widget.screenPreviewOn || !widget.connected) {
      return _buildPreview();
    }

    return OrientationBuilder(
      builder: (context, orientation) {
        if (orientation == Orientation.landscape) {
          // Landscape: side-by-side
          return SizedBox(
            height: 260,
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Expanded(child: _buildPreview()),
                const SizedBox(width: 12),
                SizedBox(width: 240, child: _buildTrackpad()),
              ],
            ),
          );
        }
        // Portrait: stacked
        return Column(
          children: [
            _buildPreview(),
            const SizedBox(height: 12),
            _buildTrackpad(),
          ],
        );
      },
    );
  }
}
