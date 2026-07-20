import 'package:flutter/material.dart';
import '../services/connection.dart';

class TransferQueueScreen extends StatelessWidget {
  final PcConnection conn;
  final VoidCallback? onPickFiles;

  const TransferQueueScreen({
    super.key,
    required this.conn,
    this.onPickFiles,
  });

  String _formatSize(int bytes) {
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
        title: const Text('Transfer Queue'),
        actions: [
          IconButton(
            icon: const Icon(Icons.add_circle_outline_rounded),
            tooltip: 'Send Files',
            onPressed: onPickFiles,
          ),
        ],
      ),
      body: ValueListenableBuilder<Map<String, FileTransferProgress>>(
        valueListenable: conn.activeTransfersNotifier,
        builder: (context, transfersMap, _) {
          final items = transfersMap.values.toList();

          if (items.isEmpty) {
            return Center(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  Icon(Icons.swap_horizontal_circle_outlined, size: 72, color: cs.onSurface.withValues(alpha: 0.2)),
                  const SizedBox(height: 16),
                  Text(
                    'No transfers yet',
                    style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold, color: cs.onSurface.withValues(alpha: 0.7)),
                  ),
                  const SizedBox(height: 8),
                  Text(
                    'Files sent or downloaded will appear here in real time.',
                    textAlign: TextAlign.center,
                    style: TextStyle(fontSize: 13, color: cs.onSurface.withValues(alpha: 0.5)),
                  ),
                  const SizedBox(height: 24),
                  if (onPickFiles != null)
                    FilledButton.icon(
                      onPressed: onPickFiles,
                      icon: const Icon(Icons.upload_file_rounded),
                      label: const Text('Send Files'),
                    ),
                ],
              ),
            );
          }

          final completedCount = items.where((i) => i.state == TransferState.completed).length;
          final totalBytes = items.fold<int>(0, (sum, i) => sum + i.totalBytes);

          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              // ── Batch summary header ──
              Card(
                color: cs.surfaceContainerHighest,
                elevation: 0,
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Row(
                    children: [
                      Icon(Icons.inventory_2_rounded, color: cs.primary),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              '$completedCount of ${items.length} files transferred',
                              style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14),
                            ),
                            Text(
                              'Total batch volume: ${_formatSize(totalBytes)}',
                              style: TextStyle(fontSize: 12, color: cs.onSurfaceVariant),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 16),

              // ── Transfer Rows ──
              ...items.map((item) => _TransferRow(item: item, conn: conn)),
            ],
          );
        },
      ),
    );
  }
}

class _TransferRow extends StatelessWidget {
  final FileTransferProgress item;
  final PcConnection conn;

  const _TransferRow({required this.item, required this.conn});

  Color _getStatusColor(BuildContext context, TransferState state) {
    final cs = Theme.of(context).colorScheme;
    switch (state) {
      case TransferState.queued:
        return Colors.grey;
      case TransferState.active:
        return cs.primary;
      case TransferState.paused:
        return Colors.orange;
      case TransferState.completed:
        return Colors.green;
      case TransferState.failed:
        return cs.error;
    }
  }

  @override
  Widget build(BuildContext context) {
    final cs = Theme.of(context).colorScheme;
    final statusColor = _getStatusColor(context, item.state);

    return Semantics(
      label: item.semanticsLabel,
      child: Card(
        margin: const EdgeInsets.only(bottom: 12),
        child: Padding(
          padding: const EdgeInsets.all(14),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                children: [
                  Icon(
                    item.isDownload ? Icons.download_rounded : Icons.upload_rounded,
                    color: statusColor,
                    size: 22,
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          item.filename,
                          style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 14),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                        if (item.suffixedFilename != null)
                          Text(
                            'Saved as ${item.suffixedFilename} (file already existed)',
                            style: const TextStyle(fontSize: 11, color: Colors.orange, fontWeight: FontWeight.w500),
                          ),
                      ],
                    ),
                  ),
                  Semantics(
                    label: 'Status: ${item.state.name}',
                    child: Container(
                      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
                      decoration: BoxDecoration(
                        color: statusColor.withValues(alpha: 0.15),
                        borderRadius: BorderRadius.circular(12),
                      ),
                      child: Text(
                        item.state.name.toUpperCase(),
                        style: TextStyle(
                          fontSize: 10,
                          fontWeight: FontWeight.bold,
                          color: statusColor,
                        ),
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 10),

              if (item.state == TransferState.active) ...[
                ClipRRect(
                  borderRadius: BorderRadius.circular(4),
                  child: LinearProgressIndicator(
                    value: item.progress > 0 ? item.progress : null,
                    minHeight: 6,
                  ),
                ),
                const SizedBox(height: 6),
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Text(
                      '${item.progressStr} • ${item.speedStr}',
                      style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w500),
                    ),
                    Text(
                      'ETA ${item.etaStr}',
                      style: TextStyle(fontSize: 12, color: cs.onSurfaceVariant),
                    ),
                  ],
                ),
              ],

              if (item.state == TransferState.failed && item.error != null) ...[
                Text(
                  'Error: ${item.error}',
                  style: TextStyle(fontSize: 12, color: cs.error),
                ),
                const SizedBox(height: 6),
              ],

              // Row action buttons
              Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  if (item.state == TransferState.failed)
                    Semantics(
                      button: true,
                      label: 'Retry transfer for ${item.filename}',
                      child: TextButton.icon(
                        icon: const Icon(Icons.refresh_rounded, size: 16),
                        label: const Text('Retry'),
                        onPressed: () {
                          // Retry transfer trigger
                          conn.uploadFile(item.filename, onProgress: (_) {}, resumeTransferId: item.id);
                        },
                      ),
                    ),
                  if (item.state == TransferState.active || item.state == TransferState.queued)
                    Semantics(
                      button: true,
                      label: 'Cancel transfer for ${item.filename}',
                      child: TextButton.icon(
                        icon: Icon(Icons.cancel_outlined, size: 16, color: cs.error),
                        label: Text('Cancel', style: TextStyle(color: cs.error)),
                        onPressed: () => conn.abortTransfer(item.id),
                      ),
                    ),
                  if (item.state == TransferState.completed)
                    TextButton.icon(
                      icon: const Icon(Icons.check_circle_outline_rounded, size: 16, color: Colors.green),
                      label: const Text('Completed', style: TextStyle(color: Colors.green)),
                      onPressed: () {},
                    ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }
}
