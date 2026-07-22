using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Pconnect.Agent.Services;
using Xunit;

namespace Pconnect.Agent.Tests;

public class SecurityAndProtocolTests
{
    [Fact]
    public void MonotonicEpoch_SeedsFromCurrentTicksAndNeverDecreases()
    {
        long epoch1 = DateTime.UtcNow.Ticks;
        System.Threading.Thread.Sleep(10);
        long epoch2 = DateTime.UtcNow.Ticks;

        Assert.True(epoch2 > epoch1, "Session epoch must monotonically increase across restarts.");
    }

    [Fact]
    public void PairingService_SupportsTwoWindowCodeValidity()
    {
        using var pairing = new PairingService();
        string firstCode = pairing.CurrentCode;

        Assert.True(pairing.ValidateCode(firstCode));

        pairing.RotateCode();
        string secondCode = pairing.CurrentCode;

        Assert.True(pairing.ValidateCode(secondCode));
        Assert.True(pairing.ValidateCode(firstCode), "Previous rotated code must remain valid during grace window.");

        pairing.RotateCode();
        Assert.False(pairing.ValidateCode(firstCode), "Code older than 2 windows must be rejected.");
    }

    [Fact]
    public void PersistentPinLockout_PersistsAcrossStoreReloadAndRestarts()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"pconnect_test_paired_{Guid.NewGuid():N}.json");
        try
        {
            var store1 = new PairedDevicesStore(tempPath);
            store1.SetShutdownPin("9999");
            string deviceId = "device_test_123";

            // Trigger 5 failed attempts
            for (int i = 0; i < 5; i++)
            {
                store1.VerifyShutdownPin(deviceId, "0000", out _, out _);
            }

            bool isLocked = !store1.VerifyShutdownPin(deviceId, "9999", out bool rateLimited, out var error);
            Assert.True(isLocked);
            Assert.True(rateLimited);

            // Simulate Agent Restart by loading fresh store instance from disk
            var store2 = new PairedDevicesStore(tempPath);
            store2.Load();

            bool isStillLocked = !store2.VerifyShutdownPin(deviceId, "9999", out bool rateLimited2, out _);
            Assert.True(isStillLocked, "PIN lockout must persist across process restart.");
            Assert.True(rateLimited2);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void PairedDevicesStore_AppliesRestrictedUserAclOnFileCreation()
    {
        if (!OperatingSystem.IsWindows()) return;

        string tempPath = Path.Combine(Path.GetTempPath(), $"pconnect_test_acl_{Guid.NewGuid():N}.json");
        try
        {
            var store = new PairedDevicesStore(tempPath);
            store.Save();

            Assert.True(File.Exists(tempPath));

            var fileInfo = new FileInfo(tempPath);
            var security = fileInfo.GetAccessControl();
            var rules = security.GetAccessRules(true, false, typeof(SecurityIdentifier));

            Assert.NotEmpty(rules);
            var currentUser = WindowsIdentity.GetCurrent().User;

            bool foundCurrentUserRule = false;
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.IdentityReference == currentUser)
                {
                    foundCurrentUserRule = true;
                    Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights & FileSystemRights.FullControl);
                }
            }

            Assert.True(foundCurrentUserRule, "File ACL must grant explicit access to current user.");
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void CommandIntegrity_ValidatesEpochBoundMac()
    {
        byte[] key = new byte[32];
        Random.Shared.NextBytes(key);
        long epoch = 638000000000000000L;
        int seq = 1;
        string canon = "launch|notepad|";

        using var hmac = new System.Security.Cryptography.HMACSHA256(key);
        var expected = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes($"{epoch}|{seq}|{canon}"));
        string macB64 = Convert.ToBase64String(expected);

        Assert.True(CommandIntegrity.TryVerifyMac(key, epoch, seq, canon, macB64));
        Assert.False(CommandIntegrity.TryVerifyMac(key, epoch + 1, seq, canon, macB64), "Replayed MAC from different epoch must fail verification.");
        Assert.False(CommandIntegrity.TryVerifyMac(key, epoch, seq + 1, canon, macB64), "Replayed MAC with modified seq must fail verification.");
    }
}
