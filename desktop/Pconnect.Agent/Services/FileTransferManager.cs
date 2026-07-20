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
        public long UnflushedBytes { get; set; }
        public DateTime LastProgressReport { get; set; } = DateTime.MinValue;
        public string? ExpectedSha256 { get; set; }
    }

    public enum TransferStatus { Queued, Active, Paused, Completed, Failed }

    public class QueueItem
    {
        public string TransferId { get; set; } = string.Empty;
        public string Filename { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Direction { get; set; } = "upload"; // "upload" or "download"
        public TransferStatus Status { get; set; } = TransferStatus.Queued;
        public string? Error { get; set; }
    }

    private class TransferStateDto
    {
        public string TransferId { get; set; } = string.Empty;
        public string TempFilePath { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public long ReceivedBytes { get; set; }
        public int ChunkSize { get; set; }
        public int TotalChunks { get; set; }
        public List<int> ReceivedChunks { get; set; } = new();
        public string? ExpectedSha256 { get; set; }
    }

    public long FlushThresholdBytes { get; set; } = 2 * 1024 * 1024; // 2MB flush threshold

    private readonly Dictionary<string, ActiveTransfer> _activeTransfers = new();
    private readonly Dictionary<string, QueueItem> _queueItems = new();
    private readonly object _lock = new();
    private readonly string _stateFilePath;
    private System.Threading.Timer? _cleanupTimer;

    public FileTransferManager()
    {
        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pconnect");
        Directory.CreateDirectory(appData);
        _stateFilePath = Path.Combine(appData, "transfers.json");

        LoadState();

        // Cleanup abandoned transfers every 5 minutes
        _cleanupTimer = new System.Threading.Timer(_ => CleanupExpired(), null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    /// <summary>
    /// Initiates a file transfer with disk space check and filename collision resolution.
    /// </summary>
    public string? StartTransfer(string transferId, string filename, long size)
    {
        return StartTransfer(transferId, filename, size, out _);
    }

    public string? StartTransfer(string transferId, string filename, long size, out string? error)
    {
        return StartTransfer(transferId, filename, size, null, out error);
    }

    public string? StartTransfer(string transferId, string filename, long size, string? expectedSha256, out string? error)
    {
        error = null;
        lock (_lock)
        {
            if (_activeTransfers.ContainsKey(transferId))
            {
                error = "Transfer already exists";
                return null;
            }

            try
            {
                var downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                Directory.CreateDirectory(downloadFolder);

                // 1. Disk space precheck
                var rootPath = Path.GetPathRoot(downloadFolder);
                if (!string.IsNullOrEmpty(rootPath))
                {
                    try
                    {
                        var drive = new DriveInfo(rootPath);
                        if (drive.AvailableFreeSpace < size)
                        {
                            error = $"Insufficient disk space (required: {size} bytes, available: {drive.AvailableFreeSpace} bytes)";
                            return null;
                        }
                    }
                    catch { /* DriveInfo failure safeguard */ }
                }

                // 2. Filename collision auto-suffixing
                var sanitized = SanitizeFilename(filename);
                var targetPath = GetNonCollidingPath(downloadFolder, sanitized);

                var tempFile = Path.GetTempFileName();
                var chunkSize = 50 * 1024;
                var transfer = new ActiveTransfer
                {
                    TempFilePath = tempFile,
                    TotalChunks = (int)Math.Ceiling((double)size / chunkSize),
                    ChunkSize = chunkSize,
                    TotalBytes = size,
                    TargetPath = targetPath,
                    ExpectedSha256 = expectedSha256,
                    FileStream = new FileStream(tempFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024)
                };

                _activeTransfers[transferId] = transfer;
                SaveState();
                return transferId;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return null;
            }
        }
    }

    /// <summary>
    /// Resumes an existing transfer, returning the highest contiguous received chunk index and list of received chunks.
    /// </summary>
    public bool ResumeTransfer(string transferId, string filename, long size, string? expectedSha256, out int highestContiguousChunk, out HashSet<int> receivedChunks, out string? error)
    {
        error = null;
        highestContiguousChunk = -1;
        receivedChunks = new HashSet<int>();

        lock (_lock)
        {
            if (!_activeTransfers.TryGetValue(transferId, out var transfer))
            {
                var res = StartTransfer(transferId, filename, size, expectedSha256, out error);
                return res != null;
            }

            try
            {
                if (transfer.FileStream == null && File.Exists(transfer.TempFilePath))
                {
                    transfer.FileStream = new FileStream(transfer.TempFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 64 * 1024);
                }

                if (!string.IsNullOrWhiteSpace(expectedSha256))
                {
                    transfer.ExpectedSha256 = expectedSha256;
                }

                transfer.LastActivity = DateTime.UtcNow;
                receivedChunks = new HashSet<int>(transfer.ReceivedChunks);

                int idx = 0;
                while (receivedChunks.Contains(idx))
                {
                    idx++;
                }
                highestContiguousChunk = idx - 1;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
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
                
                transfer.ReceivedChunks.Add(chunkIndex);
                transfer.ReceivedBytes += data.Length;
                transfer.LastActivity = DateTime.UtcNow;

                transfer.UnflushedBytes += data.Length;
                if (transfer.UnflushedBytes >= FlushThresholdBytes)
                {
                    transfer.FileStream.Flush(flushToDisk: true);
                    transfer.UnflushedBytes = 0;
                    SaveState();
                }
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Checks whether progress should be reported (throttled to at most every intervalMs).
    /// </summary>
    public bool ShouldReportProgress(string transferId, int intervalMs = 250)
    {
        lock (_lock)
        {
            if (_activeTransfers.TryGetValue(transferId, out var transfer))
            {
                var now = DateTime.UtcNow;
                if (transfer.ReceivedBytes >= transfer.TotalBytes || (now - transfer.LastProgressReport).TotalMilliseconds >= intervalMs)
                {
                    transfer.LastProgressReport = now;
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Completes a transfer and moves the temp file to the target location.
    /// </summary>
    public bool CompleteTransfer(string transferId)
    {
        return CompleteTransfer(transferId, out _);
    }

    public bool CompleteTransfer(string transferId, out string? finalPath)
    {
        finalPath = null;
        lock (_lock)
        {
            if (!_activeTransfers.TryGetValue(transferId, out var transfer))
            {
                return false;
            }

            try
            {
                transfer.FileStream?.Flush(flushToDisk: true);
                transfer.FileStream?.Dispose();
                transfer.FileStream = null;

                // Verify file completeness by checking actual file size
                var actualSize = new FileInfo(transfer.TempFilePath).Length;
                if (actualSize != transfer.TotalBytes)
                {
                    File.Delete(transfer.TempFilePath);
                    _activeTransfers.Remove(transferId);
                    SaveState();
                    return false;
                }

                // Integrity verification: verify expected SHA-256 hash if provided
                if (!string.IsNullOrWhiteSpace(transfer.ExpectedSha256))
                {
                    using var sha256 = SHA256.Create();
                    using var stream = File.OpenRead(transfer.TempFilePath);
                    var hashBytes = sha256.ComputeHash(stream);
                    var computedHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
                    if (!string.Equals(computedHex, transfer.ExpectedSha256.Trim().ToLowerInvariant(), StringComparison.Ordinal))
                    {
                        File.Delete(transfer.TempFilePath);
                        _activeTransfers.Remove(transferId);
                        SaveState();
                        return false;
                    }
                }

                // Ensure target file path is non-colliding (auto-suffix if file was created during transfer)
                var finalTarget = GetNonCollidingPath(Path.GetDirectoryName(transfer.TargetPath)!, Path.GetFileName(transfer.TargetPath));
                File.Move(transfer.TempFilePath, finalTarget);
                finalPath = finalTarget;
                _activeTransfers.Remove(transferId);
                SaveState();
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
                SaveState();
                return false;
            }
        }
    }

    private void SaveState()
    {
        try
        {
            var list = _activeTransfers.Select(kvp => new TransferStateDto
            {
                TransferId = kvp.Key,
                TempFilePath = kvp.Value.TempFilePath,
                TargetPath = kvp.Value.TargetPath,
                TotalBytes = kvp.Value.TotalBytes,
                ReceivedBytes = kvp.Value.ReceivedBytes,
                ChunkSize = kvp.Value.ChunkSize,
                TotalChunks = kvp.Value.TotalChunks,
                ReceivedChunks = kvp.Value.ReceivedChunks.ToList(),
                ExpectedSha256 = kvp.Value.ExpectedSha256
            }).ToList();

            var json = JsonSerializer.Serialize(list);
            File.WriteAllText(_stateFilePath, json);
        }
        catch { }
    }

    private void LoadState()
    {
        try
        {
            if (!File.Exists(_stateFilePath)) return;
            var json = File.ReadAllText(_stateFilePath);
            var list = JsonSerializer.Deserialize<List<TransferStateDto>>(json);
            if (list == null) return;

            foreach (var item in list)
            {
                if (File.Exists(item.TempFilePath))
                {
                    _activeTransfers[item.TransferId] = new ActiveTransfer
                    {
                        TempFilePath = item.TempFilePath,
                        TargetPath = item.TargetPath,
                        TotalBytes = item.TotalBytes,
                        ReceivedBytes = item.ReceivedBytes,
                        ChunkSize = item.ChunkSize,
                        TotalChunks = item.TotalChunks,
                        ReceivedChunks = new HashSet<int>(item.ReceivedChunks),
                        ExpectedSha256 = item.ExpectedSha256
                    };
                }
            }
        }
        catch { }
    }

    public static bool IsPathInAllowlist(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        try
        {
            var fullPath = Path.GetFullPath(filePath);
            var desktop = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Desktop)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var documents = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var downloads = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return fullPath.StartsWith(desktop, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(documents, StringComparison.OrdinalIgnoreCase) ||
                   fullPath.StartsWith(downloads, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public object ListAllowedDirectory(string? path)
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

        if (string.IsNullOrWhiteSpace(path))
        {
            return new[]
            {
                new { name = "Desktop", path = desktop, isDir = true, size = 0L },
                new { name = "Documents", path = documents, isDir = true, size = 0L },
                new { name = "Downloads", path = downloads, isDir = true, size = 0L },
            };
        }

        if (!IsPathInAllowlist(path) || !Directory.Exists(path))
        {
            return Array.Empty<object>();
        }

        var dirInfo = new DirectoryInfo(path);
        var items = new List<object>();

        foreach (var d in dirInfo.GetDirectories())
        {
            if ((d.Attributes & FileAttributes.Hidden) != 0) continue;
            items.Add(new { name = d.Name, path = d.FullName, isDir = true, size = 0L });
        }

        foreach (var f in dirInfo.GetFiles())
        {
            if ((f.Attributes & FileAttributes.Hidden) != 0) continue;
            items.Add(new { name = f.Name, path = f.FullName, isDir = false, size = f.Length });
        }

        return items;
    }

    public bool StartDownload(string transferId, string filePath, out string filename, out long size, out string sha256, out string? error)
    {
        filename = string.Empty;
        size = 0;
        sha256 = string.Empty;
        error = null;

        lock (_lock)
        {
            if (!IsPathInAllowlist(filePath))
            {
                error = "Access denied: Path outside allowed directories (Desktop, Documents, Downloads)";
                return false;
            }

            if (!File.Exists(filePath))
            {
                error = "File not found";
                return false;
            }

            try
            {
                var fileInfo = new FileInfo(filePath);
                filename = SanitizeFilename(fileInfo.Name);
                size = fileInfo.Length;

                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    var hashBytes = sha.ComputeHash(stream);
                    sha256 = Convert.ToHexString(hashBytes).ToLowerInvariant();
                }

                _queueItems[transferId] = new QueueItem
                {
                    TransferId = transferId,
                    Filename = filename,
                    Size = size,
                    Direction = "download",
                    Status = TransferStatus.Active
                };

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    public byte[]? ReadDownloadChunk(string filePath, int chunkIndex, int chunkSize = 50 * 1024)
    {
        if (!IsPathInAllowlist(filePath) || !File.Exists(filePath)) return null;

        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long offset = (long)chunkIndex * chunkSize;
            if (offset >= fs.Length) return Array.Empty<byte>();

            fs.Seek(offset, SeekOrigin.Begin);
            int bytesToRead = (int)Math.Min(chunkSize, fs.Length - offset);
            byte[] buffer = new byte[bytesToRead];
            int read = fs.Read(buffer, 0, bytesToRead);
            if (read < bytesToRead)
            {
                Array.Resize(ref buffer, read);
            }
            return buffer;
        }
        catch
        {
            return null;
        }
    }

    public static string GetNonCollidingPath(string folder, string filename)
    {
        var targetPath = Path.Combine(folder, filename);
        if (!File.Exists(targetPath))
        {
            return targetPath;
        }

        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        var ext = Path.GetExtension(filename);

        int counter = 1;
        while (true)
        {
            var candidate = Path.Combine(folder, $"{nameWithoutExt} ({counter}){ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
            counter++;
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
                SaveState();
            }
        }
    }

    /// <summary>
    /// Discards a saved/active transfer, deleting the temporary file and removing state immediately.
    /// </summary>
    public bool DiscardTransfer(string transferId)
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
                SaveState();
                return true;
            }
            return false;
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
            SaveState();
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
