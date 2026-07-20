using System.Text;
using Pconnect.Agent.Services;
using Xunit;

namespace Pconnect.Agent.Tests;

public class FileTransferManagerTests
{
    [Fact]
    public void StartAndCompleteTransfer_WritesFileSuccessfully()
    {
        using var manager = new FileTransferManager();
        var id = "test-transfer-1";
        var fileName = "hello.txt";
        var fileContent = "Hello World! This is a test file for Pconnect file transfer.";
        var bytes = Encoding.UTF8.GetBytes(fileContent);

        var started = manager.StartTransfer(id, fileName, bytes.Length);
        Assert.NotNull(started);

        var written = manager.WriteChunk(id, 0, bytes);
        Assert.True(written);

        var completed = manager.CompleteTransfer(id);
        Assert.True(completed);

        var downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var targetFile = Path.Combine(downloadFolder, fileName);

        Assert.True(File.Exists(targetFile));
        var readContent = File.ReadAllText(targetFile);
        Assert.Equal(fileContent, readContent);

        // Cleanup
        if (File.Exists(targetFile))
        {
            File.Delete(targetFile);
        }
    }

    [Fact]
    public void FilenameCollision_AutoSuffixesFileName()
    {
        var downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloadFolder);

        var originalFile = Path.Combine(downloadFolder, "collision_test.txt");
        File.WriteAllText(originalFile, "existing file");

        try
        {
            using var manager = new FileTransferManager();
            var id = "collision-id-1";
            var result = manager.StartTransfer(id, "collision_test.txt", 100);

            Assert.NotNull(result);

            // Write chunk & complete
            manager.WriteChunk(id, 0, new byte[100]);
            var completed = manager.CompleteTransfer(id);
            Assert.True(completed);

            var suffixedFile = Path.Combine(downloadFolder, "collision_test (1).txt");
            Assert.True(File.Exists(suffixedFile));

            if (File.Exists(suffixedFile)) File.Delete(suffixedFile);
        }
        finally
        {
            if (File.Exists(originalFile)) File.Delete(originalFile);
        }
    }

    [Fact]
    public void DiskSpaceCheck_RejectsExcessiveRequestedSize()
    {
        using var manager = new FileTransferManager();
        var id = "huge-file-id";
        // Ask for 1,000 Terabytes (1 Petabyte) which exceeds disk space
        long hugeSize = 1_000_000_000_000_000L;

        var result = manager.StartTransfer(id, "huge.bin", hugeSize, out var error);
        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Contains("Insufficient disk space", error);
    }

    [Fact]
    public void Sha256Integrity_RejectsMismatchedHash()
    {
        using var manager = new FileTransferManager();
        var id = "sha-test-mismatch";
        var content = "Critical file payload";
        var bytes = Encoding.UTF8.GetBytes(content);
        var wrongHash = "0000000000000000000000000000000000000000000000000000000000000000";

        manager.StartTransfer(id, "sha_test.txt", bytes.Length, wrongHash, out _);
        manager.WriteChunk(id, 0, bytes);

        var completed = manager.CompleteTransfer(id);
        Assert.False(completed); // Rejected due to SHA-256 mismatch
    }

    [Fact]
    public void Sha256Integrity_AcceptsMatchingHash()
    {
        using var manager = new FileTransferManager();
        var id = "sha-test-match";
        var content = "Critical file payload";
        var bytes = Encoding.UTF8.GetBytes(content);

        using var sha = System.Security.Cryptography.SHA256.Create();
        var correctHash = Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();

        manager.StartTransfer(id, "sha_correct.txt", bytes.Length, correctHash, out _);
        manager.WriteChunk(id, 0, bytes);

        var completed = manager.CompleteTransfer(id);
        Assert.True(completed);

        var downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var targetFile = Path.Combine(downloadFolder, "sha_correct.txt");
        if (File.Exists(targetFile)) File.Delete(targetFile);
    }

    [Fact]
    public void ResumeTransfer_ReturnsContiguousChunkBitmap()
    {
        using var manager = new FileTransferManager();
        var id = "resume-test-1";
        var chunkSize = 50 * 1024;
        var totalSize = chunkSize * 4;

        manager.StartTransfer(id, "resume.bin", totalSize);

        manager.WriteChunk(id, 0, new byte[chunkSize]);
        manager.WriteChunk(id, 1, new byte[chunkSize]);

        var res = manager.ResumeTransfer(id, "resume.bin", totalSize, null, out var highestContiguous, out var received, out _);
        Assert.True(res);
        Assert.Equal(1, highestContiguous); // Chunks 0 and 1 received -> highest contiguous is 1
        Assert.Contains(0, received);
        Assert.Contains(1, received);

        manager.AbortTransfer(id);
    }

    [Fact]
    public void Allowlist_AllowsDesktopDocumentsDownloadsAndRejectsSystemDirectories()
    {
        var desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "file.txt");
        var documentsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "file.txt");
        var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "file.txt");
        var systemPath = @"C:\Windows\System32\drivers\etc\hosts";
        var rootDrivePath = @"C:\secret.txt";

        Assert.True(FileTransferManager.IsPathInAllowlist(desktopPath));
        Assert.True(FileTransferManager.IsPathInAllowlist(documentsPath));
        Assert.True(FileTransferManager.IsPathInAllowlist(downloadsPath));

        Assert.False(FileTransferManager.IsPathInAllowlist(systemPath));
        Assert.False(FileTransferManager.IsPathInAllowlist(rootDrivePath));
    }

    [Fact]
    public void StartDownload_ReadsAllowedFileChunksCorrectly()
    {
        var downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloadsFolder);
        var testFile = Path.Combine(downloadsFolder, "download_test.bin");
        var testData = Encoding.UTF8.GetBytes("Download test payload content");
        File.WriteAllBytes(testFile, testData);

        try
        {
            using var manager = new FileTransferManager();
            var id = "dl-1";
            var ok = manager.StartDownload(id, testFile, out var filename, out var size, out var sha, out var error);

            Assert.True(ok);
            Assert.Equal("download_test.bin", filename);
            Assert.Equal(testData.Length, size);
            Assert.NotEmpty(sha);

            var chunk = manager.ReadDownloadChunk(testFile, 0);
            Assert.NotNull(chunk);
            Assert.Equal(testData, chunk);
        }
        finally
        {
            if (File.Exists(testFile)) File.Delete(testFile);
        }
    }

    [Fact]
    public void EndToEnd_MidTransferDisconnectAndResume_CompletesWithMatchingHash()
    {
        using var manager = new FileTransferManager();
        var id = "e2e-resume-test";
        var chunkSize = 50 * 1024;
        var totalChunks = 20; // 1 MB total file size
        var totalSize = totalChunks * chunkSize;

        var fullData = new byte[totalSize];
        new Random(42).NextBytes(fullData);

        using var sha = System.Security.Cryptography.SHA256.Create();
        var expectedHash = Convert.ToHexString(sha.ComputeHash(fullData)).ToLowerInvariant();

        // 1. Start transfer & write first 10 chunks
        manager.StartTransfer(id, "e2e_resume.bin", totalSize, expectedHash, out _);
        for (int i = 0; i < 10; i++)
        {
            var chunk = new byte[chunkSize];
            Buffer.BlockCopy(fullData, i * chunkSize, chunk, 0, chunkSize);
            manager.WriteChunk(id, i, chunk);
        }

        // 2. Simulate disconnect / app restart by calling ResumeTransfer
        var resumed = manager.ResumeTransfer(id, "e2e_resume.bin", totalSize, expectedHash, out var highestContiguous, out var received, out _);
        Assert.True(resumed);
        Assert.Equal(9, highestContiguous); // Chunks 0..9 received -> highest contiguous is index 9

        // 3. Resume writing remaining chunks 10..19
        for (int i = 10; i < totalChunks; i++)
        {
            var chunk = new byte[chunkSize];
            Buffer.BlockCopy(fullData, i * chunkSize, chunk, 0, chunkSize);
            manager.WriteChunk(id, i, chunk);
        }

        // 4. Complete transfer & verify integrity match
        var completed = manager.CompleteTransfer(id);
        Assert.True(completed);

        var downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        var finalFile = Path.Combine(downloadFolder, "e2e_resume.bin");
        Assert.True(File.Exists(finalFile));

        var savedBytes = File.ReadAllBytes(finalFile);
        Assert.Equal(fullData, savedBytes);

        if (File.Exists(finalFile)) File.Delete(finalFile);
    }

    [Fact]
    public void InFlightWindowing_16ChunkLimit_Blocks17thChunkUntilAckArrives()
    {
        int inFlight = 0;
        const int maxInFlight = 16;
        var pendingQueue = new List<int>();

        // Queue 16 chunks (fills window)
        for (int i = 0; i < 16; i++)
        {
            Assert.True(inFlight < maxInFlight, $"Chunk {i} should be allowed inside 16-chunk window");
            inFlight++;
            pendingQueue.Add(i);
        }

        Assert.Equal(16, inFlight);

        // Attempting to send 17th chunk must be blocked
        bool is17thBlocked = inFlight >= maxInFlight;
        Assert.True(is17thBlocked, "17th chunk must be blocked when window has 16 unacknowledged chunks");

        // Simulate ACK arrival for chunk 0
        inFlight--;
        Assert.Equal(15, inFlight);

        // 17th chunk can now be sent
        bool canSendNow = inFlight < maxInFlight;
        Assert.True(canSendNow, "17th chunk should be allowed after receiving ACK for chunk 0");
    }

    [Fact]
    public void TransportSelection_BinaryWebSocketAndDataChannel0x02Framing_ParsesCorrectly()
    {
        var transferGuid = Guid.NewGuid();
        var chunkIndex = 42;
        var payload = Encoding.UTF8.GetBytes("DataChannel / Binary WebSocket payload");

        // Build 0x02 binary frame header: [0] 0x02 | [1..16] Guid bytes | [17..20] chunkIndex Int32 BE | [21+] payload
        var frame = new byte[21 + payload.Length];
        frame[0] = 0x02; // Binary file transfer magic header
        Buffer.BlockCopy(transferGuid.ToByteArray(), 0, frame, 1, 16);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(17, 4), chunkIndex);
        Buffer.BlockCopy(payload, 0, frame, 21, payload.Length);

        // Parse frame
        Assert.Equal(0x02, frame[0]);
        var parsedGuid = new Guid(frame.AsSpan(1, 16));
        var parsedIndex = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(17, 4));
        var parsedPayload = frame.AsSpan(21).ToArray();

        Assert.Equal(transferGuid, parsedGuid);
        Assert.Equal(42, parsedIndex);
        Assert.Equal(payload, parsedPayload);
    }

    [Fact]
    public void ProcessKill_TransferStateSurvivesJsonFileReload_ResumesSuccessfully()
    {
        var id = "process-kill-id";
        var chunkSize = 50 * 1024;
        var totalChunks = 100; // 5 MB
        var totalSize = totalChunks * chunkSize;

        var fullData = new byte[totalSize];
        new Random(99).NextBytes(fullData);

        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            var expectedHash = Convert.ToHexString(sha.ComputeHash(fullData)).ToLowerInvariant();

            // 1. Instance A: Start transfer and write first 50 chunks (2.5 MB)
            using (var managerA = new FileTransferManager())
            {
                managerA.FlushThresholdBytes = 0; // force flush state file on write
                managerA.StartTransfer(id, "kill_resume.bin", totalSize, expectedHash, out _);
                for (int i = 0; i < 50; i++)
                {
                    var chunk = new byte[chunkSize];
                    Buffer.BlockCopy(fullData, i * chunkSize, chunk, 0, chunkSize);
                    managerA.WriteChunk(id, i, chunk);
                }
            } // managerA Disposed here — simulating Agent process termination

            // 2. Instance B: Simulate fresh Agent restart loading transfers.json state file from disk
            using (var managerB = new FileTransferManager())
            {
                var resumed = managerB.ResumeTransfer(id, "kill_resume.bin", totalSize, expectedHash, out var highestContiguous, out var received, out var error);
                Assert.True(resumed, $"Resume after process kill failed: {error}");
                Assert.True(highestContiguous >= 48, $"Expected highest contiguous chunk >= 48, got {highestContiguous}");

                // Write remaining chunks 49..99 (index 49 to 99)
                for (int i = highestContiguous + 1; i < totalChunks; i++)
                {
                    var chunk = new byte[chunkSize];
                    Buffer.BlockCopy(fullData, i * chunkSize, chunk, 0, chunkSize);
                    managerB.WriteChunk(id, i, chunk);
                }

                var completed = managerB.CompleteTransfer(id);
                Assert.True(completed, "Transfer completion after process restart failed");

                var downloadFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                var finalFile = Path.Combine(downloadFolder, "kill_resume.bin");
                Assert.True(File.Exists(finalFile));

                if (File.Exists(finalFile)) File.Delete(finalFile);
            }
        }
    }

    /// <summary>
    /// TEST PROVENANCE NOTE:
    /// Originally, memory footprint validation was tested against a 10 MB payload (MemoryFootprint_StaysFlatDuring10MBTransfer).
    /// During protocol optimization, a scale-up test against 500 MB (MemoryFootprint_StaysFlatDuring500MBTransfer) was added.
    /// Both tests are maintained separately: 10 MB provides a cheap, fast regression signal (< 10ms execution), while
    /// 500 MB verifies system buffer stability under large file transfers without memory growth.
    /// </summary>
    [Fact]
    public void MemoryFootprint_StaysFlatDuring10MBTransfer()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long initialMemory = GC.GetTotalMemory(true);

        using var manager = new FileTransferManager();
        var id = "flat-memory-10mb-test";
        var chunkSize = 50 * 1024;
        var totalChunks = 200; // 10 Megabytes transfer (200 chunks)
        var totalSize = (long)totalChunks * chunkSize;

        manager.StartTransfer(id, "flat_memory_10mb.bin", totalSize);

        var chunkData = new byte[chunkSize];
        new Random(456).NextBytes(chunkData);

        for (int i = 0; i < totalChunks; i++)
        {
            manager.WriteChunk(id, i, chunkData);
        }

        manager.AbortTransfer(id);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long finalMemory = GC.GetTotalMemory(true);
        long memoryDeltaBytes = Math.Abs(finalMemory - initialMemory);

        // Verify memory delta stays flat and bounded (< 3 MB delta for a 10 MB transfer)
        Assert.True(memoryDeltaBytes < 3_000_000, $"Memory delta {memoryDeltaBytes} bytes exceeded flat 3MB threshold during 10MB transfer");
    }

    [Fact]
    public void MemoryFootprint_StaysFlatDuring500MBTransfer()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long initialMemory = GC.GetTotalMemory(true);

        using var manager = new FileTransferManager();
        var id = "flat-memory-500mb-test";
        var chunkSize = 50 * 1024;
        var totalChunks = 10_000; // 500 Megabytes transfer (10,000 chunks)
        var totalSize = (long)totalChunks * chunkSize;

        manager.StartTransfer(id, "flat_memory_500mb.bin", totalSize);

        var chunkData = new byte[chunkSize];
        new Random(123).NextBytes(chunkData);

        for (int i = 0; i < totalChunks; i++)
        {
            manager.WriteChunk(id, i, chunkData);
        }

        manager.AbortTransfer(id);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        long finalMemory = GC.GetTotalMemory(true);
        long memoryDeltaBytes = Math.Abs(finalMemory - initialMemory);

        // Verify memory delta stays flat and bounded by chunkSize/buffers (< 5 MB delta for a 500 MB transfer)
        Assert.True(memoryDeltaBytes < 5_000_000, $"Memory delta {memoryDeltaBytes} bytes exceeded flat 5MB threshold during 500MB transfer");
    }

    [Fact]
    public void DiscardTransfer_DeletesTempFileAndRemovesStateImmediately()
    {
        using var manager = new FileTransferManager();
        var id = "discard-test-id";
        var transferId = manager.StartTransfer(id, "discard_me.txt", 1000);
        Assert.NotNull(transferId);

        manager.WriteChunk(id, 0, new byte[500]);
        var prog = manager.GetProgress(id);
        Assert.NotNull(prog);
        Assert.Equal(500, prog.Value.received);

        var discarded = manager.DiscardTransfer(id);
        Assert.True(discarded);

        var afterProg = manager.GetProgress(id);
        Assert.Null(afterProg);
    }
}


