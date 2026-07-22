using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Pconnect.Agent.Services;

internal sealed class PairedDevicesStore
{
    private readonly object _gate = new();
    private readonly string _path;

    private ConcurrentDictionary<string, string> _tokensByDeviceId = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, string> _namesByDeviceId = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, string> _rolesByDeviceId = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, bool> _autoLockByDeviceId = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, int> _failedPinAttemptsByDeviceId = new(StringComparer.Ordinal);
    private ConcurrentDictionary<string, DateTimeOffset> _lockoutUntilByDeviceId = new(StringComparer.Ordinal);

    private string? _shutdownPinHash;
    private string? _shutdownPinSalt;

    private const int Pbkdf2Iterations = 210000;

    public PairedDevicesStore(string? customPath = null)
    {
        if (customPath != null)
        {
            _path = customPath;
            return;
        }
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pconnect");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "paired-devices.json");
    }

    public void Load()
    {
        TryLoad(out _);
    }

    public bool TryLoad([NotNullWhen(false)] out string? error)
    {
        error = null;
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_path))
                {
                    ResetInMemoryData();
                    return true;
                }

                var json = File.ReadAllText(_path);
                var data = JsonSerializer.Deserialize<PairedDevicesFile>(json);

                _tokensByDeviceId = new ConcurrentDictionary<string, string>(data?.TokensByDeviceId ?? new(), StringComparer.Ordinal);
                _namesByDeviceId = new ConcurrentDictionary<string, string>(data?.NamesByDeviceId ?? new(), StringComparer.Ordinal);
                _rolesByDeviceId = new ConcurrentDictionary<string, string>(data?.RolesByDeviceId ?? new(), StringComparer.Ordinal);
                _autoLockByDeviceId = new ConcurrentDictionary<string, bool>(data?.AutoLockByDeviceId ?? new(), StringComparer.Ordinal);
                _failedPinAttemptsByDeviceId = new ConcurrentDictionary<string, int>(data?.FailedPinAttemptsByDeviceId ?? new(), StringComparer.Ordinal);
                
                var lockoutDict = new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
                if (data?.LockoutUntilByDeviceId != null)
                {
                    foreach (var (k, v) in data.LockoutUntilByDeviceId)
                    {
                        if (DateTimeOffset.TryParse(v, out var dto))
                        {
                            lockoutDict[k] = dto;
                        }
                    }
                }
                _lockoutUntilByDeviceId = lockoutDict;

                _shutdownPinHash = data?.ShutdownPinHash;
                _shutdownPinSalt = data?.ShutdownPinSalt;

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                ResetInMemoryData();
                return false;
            }
        }
    }

    private void ResetInMemoryData()
    {
        _tokensByDeviceId = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        _namesByDeviceId = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        _rolesByDeviceId = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);
        _autoLockByDeviceId = new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);
        _failedPinAttemptsByDeviceId = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        _lockoutUntilByDeviceId = new ConcurrentDictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        _shutdownPinHash = null;
        _shutdownPinSalt = null;
    }

    public void Save()
    {
        lock (_gate)
        {
            var lockoutsFormatted = _lockoutUntilByDeviceId.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToString("o"),
                StringComparer.Ordinal
            );

            var json = JsonSerializer.Serialize(new PairedDevicesFile
            {
                TokensByDeviceId = _tokensByDeviceId.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal),
                NamesByDeviceId = _namesByDeviceId.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal),
                RolesByDeviceId = _rolesByDeviceId.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal),
                AutoLockByDeviceId = _autoLockByDeviceId.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal),
                FailedPinAttemptsByDeviceId = _failedPinAttemptsByDeviceId.ToDictionary(k => k.Key, v => v.Value, StringComparer.Ordinal),
                LockoutUntilByDeviceId = lockoutsFormatted,
                ShutdownPinHash = _shutdownPinHash,
                ShutdownPinSalt = _shutdownPinSalt,
            }, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(_path, json);
            ApplyFileSecurityAcl(_path);
        }
    }

    private static void ApplyFileSecurityAcl(string path)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(path)) return;
        try
        {
            var currentUser = WindowsIdentity.GetCurrent().User;
            if (currentUser == null) return;

            var fileInfo = new FileInfo(path);
            var fileSecurity = new FileSecurity();
            fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            fileSecurity.AddAccessRule(new FileSystemAccessRule(
                currentUser,
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            fileInfo.SetAccessControl(fileSecurity);
        }
        catch
        {
            // Ignore ACL errors on non-NTFS or restricted environments
        }
    }

    public bool HasShutdownPin()
    {
        lock (_gate)
        {
            return !string.IsNullOrWhiteSpace(_shutdownPinHash) && !string.IsNullOrWhiteSpace(_shutdownPinSalt);
        }
    }

    public void SetShutdownPin(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin)) throw new ArgumentException("PIN cannot be empty.", nameof(pin));
        lock (_gate)
        {
            var saltBytes = RandomNumberGenerator.GetBytes(16);
            var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(pin.Trim()),
                saltBytes,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                32
            );

            _shutdownPinSalt = Convert.ToHexString(saltBytes);
            _shutdownPinHash = Convert.ToHexString(hashBytes);
            Save();
        }
    }

    public bool VerifyShutdownPin(string deviceId, string candidatePin, out bool rateLimited, out string? error)
    {
        rateLimited = false;
        error = null;

        lock (_gate)
        {
            if (!HasShutdownPin())
            {
                error = "Shutdown PIN is not configured on PC agent.";
                return false;
            }

            var now = DateTimeOffset.UtcNow;
            if (_lockoutUntilByDeviceId.TryGetValue(deviceId, out var lockoutTime) && now < lockoutTime)
            {
                rateLimited = true;
                var remaining = (int)Math.Ceiling((lockoutTime - now).TotalSeconds);
                error = $"Too many invalid attempts. Device locked out for {remaining} seconds.";
                return false;
            }

            var saltBytes = Convert.FromHexString(_shutdownPinSalt!);
            var expectedHashBytes = Convert.FromHexString(_shutdownPinHash!);
            var candidateHashBytes = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(candidatePin.Trim()),
                saltBytes,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                32
            );

            bool match = CryptographicOperations.FixedTimeEquals(expectedHashBytes, candidateHashBytes);

            if (match)
            {
                _failedPinAttemptsByDeviceId[deviceId] = 0;
                _lockoutUntilByDeviceId.TryRemove(deviceId, out _);
                Save();
                return true;
            }
            else
            {
                var attempts = _failedPinAttemptsByDeviceId.AddOrUpdate(deviceId, 1, (_, current) => current + 1);
                if (attempts >= 5)
                {
                    // Exponential backoff: 30s * 2^(attempts-5)
                    var backoffSeconds = 30 * (int)Math.Pow(2, Math.Min(attempts - 5, 4));
                    _lockoutUntilByDeviceId[deviceId] = now.AddSeconds(backoffSeconds);
                    rateLimited = true;
                    error = $"Too many invalid attempts. Locked out for {backoffSeconds} seconds.";
                }
                else
                {
                    error = $"Invalid shutdown PIN ({5 - attempts} attempts remaining).";
                }
                Save();
                return false;
            }
        }
    }

    public string? GetDeviceName(string deviceId)
    {
        return _namesByDeviceId.TryGetValue(deviceId, out var name) ? name : null;
    }

    public string GetRole(string deviceId)
    {
        if (_rolesByDeviceId.TryGetValue(deviceId, out var role) && !string.IsNullOrWhiteSpace(role))
        {
            return role;
        }
        return "admin";
    }

    public bool GetAutoLockOnDisconnect(string deviceId)
    {
        return _autoLockByDeviceId.TryGetValue(deviceId, out var v) && v;
    }

    public void SetAutoLockOnDisconnect(string deviceId, bool enabled)
    {
        _autoLockByDeviceId[deviceId] = enabled;
        Save();
    }

    public bool IsPaired(string deviceId, string token)
    {
        if (!_tokensByDeviceId.TryGetValue(deviceId, out var stored))
        {
            return false;
        }

        var a = Encoding.UTF8.GetBytes(stored);
        var b = Encoding.UTF8.GetBytes(token);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    public string PairNewDevice(string deviceId, string? deviceName)
    {
        var token = GenerateToken();
        _tokensByDeviceId[deviceId] = token;

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            _namesByDeviceId[deviceId] = deviceName.Trim();
        }

        if (!_rolesByDeviceId.ContainsKey(deviceId))
        {
            _rolesByDeviceId[deviceId] = "admin";
        }

        Save();
        return token;
    }

    private static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes);
    }

    private sealed class PairedDevicesFile
    {
        public Dictionary<string, string>? TokensByDeviceId { get; set; }
        public Dictionary<string, string>? NamesByDeviceId { get; set; }
        public Dictionary<string, string>? RolesByDeviceId { get; set; }
        public Dictionary<string, bool>? AutoLockByDeviceId { get; set; }
        public Dictionary<string, int>? FailedPinAttemptsByDeviceId { get; set; }
        public Dictionary<string, string>? LockoutUntilByDeviceId { get; set; }
        public string? ShutdownPinHash { get; set; }
        public string? ShutdownPinSalt { get; set; }
    }
}
