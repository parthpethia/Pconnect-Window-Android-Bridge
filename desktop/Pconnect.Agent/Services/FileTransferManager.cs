using System.Security.Cryptography;
using System.Text.Json;

namespace Pconnect.Agent.Services;

internal sealed class FileTransferManager : IDisposable
{
    private sealed class ActiveTransfer
    {
        public string TempFilePath { get; set; } = string.Empty;
        public FileStream? FileStream { get; set; }
        public int TotalChunks { get; set; }
        public int ChunkSize { get; set; }
        public HashSet<int> ReceivedChunks { get; set; } = new();
        public long TotalBytes { get; set; }
        public long ReceivedBytes { get; set; }
        public DateTime LastActivity { get; set; } = DateTime.UtcNow;
        public string TargetPath { get; set; } = string.Empty;
    }

    private readonly Dictionary<string, ActiveTransfer> _activeTransfers = new();
    private readonly object _lock = new();
    private System.Threading.Timer? _cleanupTimer;

    public FileTransferManager()
    {
        // Cleanup abandoned transfers every 5 minutes
        _cleanupTimer = new System.Threading.Timer(_ => CleanupExpired(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Initiates a file transfer. Returns the target file path or null if invalid.
    /// </summary>
    public string? StartTransfer(string transferId, string filename, long size)
    {
        lock (_lock)
        {
            if (_activeTransfers.ContainsKey(transferId))
            {
                return null; // Transfer already exists
            }

            try
            {
                var tempFile = Path.GetTempFileName();
                var downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                var targetPath = Path.Combine(downloadFolder, SanitizeFilename(filename));

                // Ensure Downloads folder exists
                Directory.CreateDirectory(downloadFolder);

                var chunkSize = 50 * 1024;
                var transfer = new ActiveTransfer
                {
                    TempFilePath = tempFile,
                    TotalChunks = (int)Math.Ceiling((double)size / chunkSize),
                    ChunkSize = chunkSize,
                    TotalBytes = size,
                    TargetPath = targetPath,
                    // Use ReadWrite so we can seek to write chunks at correct offsets
                    FileStream = new FileStream(tempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024)
                };

                _activeTransfers[transferId] = transfer;
                return transferId;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Writes a chunk to the transfer. Returns true if successful.
    /// </summary>
    public bool WriteChunk(string transferId, int chunkIndex, byte[] data)
    {
        lock (_lock)
        {
            if (!_activeTransfers.TryGetValue(transferId, out var transfer))
            {
                return false;
            }

            try
            {
                if (transfer.FileStream == null)
                {
                    return false;
                }

                // Write chunk at the correct offset to handle out-of-order arrival
                long offset = (long)chunkIndex * transfer.ChunkSize;
                transfer.FileStream.Seek(offset, SeekOrigin.Begin);
                transfer.FileStream.Write(data, 0, data.Length);
                transfer.FileStream.Flush();
                transfer.ReceivedChunks.Add(chunkIndex);
                transfer.ReceivedBytes += data.Length;
                transfer.LastActivity = DateTime.UtcNow;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Completes a transfer and moves the temp file to the target location.
    /// </summary>
    public bool CompleteTransfer(string transferId)
    {
        lock (_lock)
        {
            if (!_activeTransfers.TryGetValue(transferId, out var transfer))
            {
                return false;
            }

            try
            {
                transfer.FileStream?.Dispose();
                transfer.FileStream = null;

                // Verify file completeness by checking actual file size
                var actualSize = new FileInfo(transfer.TempFilePath).Length;
                if (actualSize != transfer.TotalBytes)
                {
                    File.Delete(transfer.TempFilePath);
                    _activeTransfers.Remove(transferId);
                    return false;
                }

                // Move temp file to target
                if (File.Exists(transfer.TargetPath))
                {
                    File.Delete(transfer.TargetPath);
                }

                File.Move(transfer.TempFilePath, transfer.TargetPath);
                _activeTransfers.Remove(transferId);
                return true;
            }
            catch
            {
                try
                {
                    File.Delete(transfer.TempFilePath);
                }
                catch { }

                _activeTransfers.Remove(transferId);
                return false;
            }
        }
    }

    /// <summary>
    /// Aborts a transfer and cleans up temp file.
    /// </summary>
    public void AbortTransfer(string transferId)
    {
        lock (_lock)
        {
            if (_activeTransfers.TryGetValue(transferId, out var transfer))
            {
                try
                {
                    transfer.FileStream?.Dispose();
                    if (File.Exists(transfer.TempFilePath))
                    {
                        File.Delete(transfer.TempFilePath);
                    }
                }
                catch { }

                _activeTransfers.Remove(transferId);
            }
        }
    }

    /// <summary>
    /// Gets transfer progress, or null if not found.
    /// </summary>
    public (long received, long total, int chunkCount)? GetProgress(string transferId)
    {
        lock (_lock)
        {
            if (_activeTransfers.TryGetValue(transferId, out var transfer))
            {
                return (transfer.ReceivedBytes, transfer.TotalBytes, transfer.ReceivedChunks.Count);
            }

            return null;
        }
    }

    private void CleanupExpired()
    {
        lock (_lock)
        {
            var expired = _activeTransfers
                .Where(kvp => DateTime.UtcNow.Subtract(kvp.Value.LastActivity).TotalMinutes > 15)
                .ToList();

            foreach (var (id, transfer) in expired)
            {
                try
                {
                    transfer.FileStream?.Dispose();
                    if (File.Exists(transfer.TempFilePath))
                    {
                        File.Delete(transfer.TempFilePath);
                    }
                }
                catch { }

                _activeTransfers.Remove(id);
            }
        }
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static string SanitizeFilename(string filename)
    {
        // Strip any directory components first (prevents ..\..\Desktop\malware.exe)
        var nameOnly = Path.GetFileName(filename);
        if (string.IsNullOrWhiteSpace(nameOnly))
            nameOnly = "unnamed_transfer";

        // Remove invalid filename characters
        var invalidChars = Path.GetInvalidFileNameChars();
        nameOnly = string.Concat(nameOnly.Split(invalidChars));

        if (string.IsNullOrWhiteSpace(nameOnly))
            nameOnly = "unnamed_transfer";

        // Reject reserved Windows device names (CON, PRN, NUL, COM1, etc.)
        var baseName = Path.GetFileNameWithoutExtension(nameOnly);
        if (ReservedNames.Contains(baseName))
            nameOnly = $"_{nameOnly}";

        // Limit total length to prevent filesystem errors
        if (nameOnly.Length > 200)
        {
            var ext = Path.GetExtension(nameOnly);
            nameOnly = nameOnly[..(200 - ext.Length)] + ext;
        }

        return nameOnly;
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();

        lock (_lock)
        {
            foreach (var transfer in _activeTransfers.Values)
            {
                try
                {
                    transfer.FileStream?.Dispose();
                }
                catch { }
            }

            _activeTransfers.Clear();
        }
    }
}
