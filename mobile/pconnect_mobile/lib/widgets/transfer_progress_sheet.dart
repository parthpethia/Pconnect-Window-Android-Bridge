import 'package:flutter/material.dart';
import '../services/connection.dart';

class TransferProgressOverlay extends StatefulWidget {
  final PcConnection conn;
  final Widget child;

  const TransferProgressOverlay({
    super.key,
    required this.conn,
    required this.child,
  });

  @override
  State<TransferProgressOverlay> createState() => _TransferProgressOverlayState();
}

class _TransferProgressOverlayState extends State<TransferProgressOverlay> {
  bool _isMinimized = false;

  String _formatSize(int bytes) {
    if (bytes < 1024) return '$bytes B';
    if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(1)} KB';
    if (bytes < 1024 * 1024 * 1024) return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
    return '${(bytes / (1024 * 1024 * 1024)).toStringAsFixed(2)} GB';
  }

  @override
  Widget build(BuildContext context) {
    return Stack(
      children: [
        widget.child,
        ValueListenableBuilder<Map<String, FileTransferProgress>>(
          valueListenable: widget.conn.activeTransfersNotifier,
          builder: (context, transfers, _) {
            final activeList = transfers.values
                .where((t) => t.state == TransferState.active || t.state == TransferState.queued)
                .toList();

            if (activeList.isEmpty) return const SizedBox.shrink();
            final current = activeList.first;

            if (_isMinimized) {
              return Positioned(
                bottom: 24,
                right: 16,
                child: Semantics(
                  label: current.semanticsLabel,
                  button: true,
                  hint: 'Double tap to expand transfer details',
                  child: Material(
                    elevation: 6,
                    borderRadius: BorderRadius.circular(24),
                    color: Theme.of(context).colorScheme.primaryContainer,
                    child: InkWell(
                      borderRadius: BorderRadius.circular(24),
                      onTap: () => setState(() => _isMinimized = false),
                      child: Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                        child: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            SizedBox(
                              width: 18,
                              height: 18,
                              child: CircularProgressIndicator(
                                value: current.progress > 0 ? current.progress : null,
                                strokeWidth: 2.5,
                                color: Theme.of(context).colorScheme.primary,
                              ),
                            ),
                            const SizedBox(width: 10),
                            ConstrainedBox(
                              constraints: const BoxConstraints(maxWidth: 130),
                              child: Text(
                                current.filename,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: TextStyle(
                                  fontSize: 12,
                                  fontWeight: FontWeight.w600,
                                  color: Theme.of(context).colorScheme.onPrimaryContainer,
                                ),
                              ),
                            ),
                            const SizedBox(width: 6),
                            Text(
                              current.speedStr,
                              style: TextStyle(
                                fontSize: 11,
                                fontWeight: FontWeight.bold,
                                color: Theme.of(context).colorScheme.primary,
                              ),
                            ),
                            const SizedBox(width: 4),
                            Icon(
                              Icons.expand_less_rounded,
                              size: 18,
                              color: Theme.of(context).colorScheme.onPrimaryContainer,
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                ),
              );
            }

            return Positioned(
              bottom: 16,
              left: 16,
              right: 16,
              child: Semantics(
                label: current.semanticsLabel,
                child: Card(
                  elevation: 8,
                  shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16)),
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            CircleAvatar(
                              radius: 18,
                              backgroundColor: Theme.of(context).colorScheme.primaryContainer,
                              child: Icon(
                                current.isDownload ? Icons.file_download_rounded : Icons.file_upload_rounded,
                                color: Theme.of(context).colorScheme.primary,
                                size: 20,
                              ),
                            ),
                            const SizedBox(width: 12),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    current.filename,
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 14),
                                  ),
                                  Text(
                                    '${_formatSize(current.transferredBytes)} of ${_formatSize(current.totalBytes)} • ${current.speedStr}',
                                    style: TextStyle(
                                      fontSize: 12,
                                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                            IconButton(
                              icon: const Icon(Icons.expand_more_rounded),
                              tooltip: 'Minimize',
                              onPressed: () => setState(() => _isMinimized = true),
                            ),
                            IconButton(
                              icon: const Icon(Icons.close_rounded),
                              tooltip: 'Cancel Transfer',
                              color: Theme.of(context).colorScheme.error,
                              onPressed: () => widget.conn.abortTransfer(current.id),
                            ),
                          ],
                        ),
                        const SizedBox(height: 12),
                        ClipRRect(
                          borderRadius: BorderRadius.circular(4),
                          child: LinearProgressIndicator(
                            value: current.progress > 0 ? current.progress : null,
                            minHeight: 6,
                          ),
                        ),
                        const SizedBox(height: 6),
                        Row(
                          mainAxisAlignment: MainAxisAlignment.spaceBetween,
                          children: [
                            Text(
                              current.progressStr,
                              style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w600),
                            ),
                            Text(
                              'ETA: ${current.etaStr}',
                              style: TextStyle(
                                fontSize: 12,
                                color: Theme.of(context).colorScheme.onSurfaceVariant,
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            );
          },
        ),
      ],
    );
  }
}
