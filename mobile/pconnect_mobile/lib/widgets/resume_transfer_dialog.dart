import 'package:flutter/material.dart';
import '../services/connection.dart';

class ResumeTransferDialog extends StatelessWidget {
  final Map<String, dynamic> savedTransfer;
  final PcConnection conn;
  final VoidCallback onDone;

  const ResumeTransferDialog({
    super.key,
    required this.savedTransfer,
    required this.conn,
    required this.onDone,
  });

  static Future<void> checkAndShow(BuildContext context, PcConnection conn) async {
    final list = await PcConnection.getSavedMobileTransfers();
    if (list.isEmpty || !context.mounted) return;

    for (final item in list) {
      if (!context.mounted) break;
      await showDialog(
        context: context,
        barrierDismissible: false,
        builder: (ctx) => ResumeTransferDialog(
          savedTransfer: item,
          conn: conn,
          onDone: () {},
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final filename = savedTransfer['filename'] as String? ?? 'file';
    final filePath = savedTransfer['filePath'] as String? ?? '';
    final id = savedTransfer['id'] as String? ?? '';

    return AlertDialog(
      title: const Row(
        children: [
          Icon(Icons.restore_page_rounded, color: Colors.amber),
          SizedBox(width: 8),
          Text('Unfinished Transfer'),
        ],
      ),
      content: Text(
        'An incomplete transfer was detected for "$filename". Would you like to resume sending this file?',
      ),
      actions: [
        TextButton(
          onPressed: () async {
            Navigator.of(context).pop();
            await conn.discardTransfer(id);
            onDone();
          },
          child: const Text('Discard', style: TextStyle(color: Colors.red)),
        ),
        FilledButton(
          onPressed: () {
            Navigator.of(context).pop();
            if (filePath.isNotEmpty) {
              conn.uploadFile(filePath, onProgress: (_) {}, resumeTransferId: id);
            }
            onDone();
          },
          child: const Text('Resume'),
        ),
      ],
    );
  }
}
