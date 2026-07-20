using System.Security.Cryptography;

namespace Pconnect.Agent.Services;

internal sealed class PairingService : IDisposable
{
    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;
    private string _currentCode = GenerateCode();

    // Rate limiting: track consecutive failures
    private int _consecutiveFailures;
    private DateTime _lockoutUntilUtc = DateTime.MinValue;
    private const int MaxAttemptsBeforeLockout = 10;
    private const int LockoutSeconds = 60;

    private DateTime _lastRotatedUtc = DateTime.UtcNow;

    public string CurrentCode
    {
        get { lock (_gate) { return _currentCode; } }
    }

    public DateTime LastRotatedUtc
    {
        get { lock (_gate) { return _lastRotatedUtc; } }
    }

    public PairingService()
    {
        // Rotate every 5 minutes by default.
        _timer = new System.Threading.Timer(_ => Rotate(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void RotateCode() => Rotate();

    public void StartRotation(TimeSpan? interval = null)
    {
        var actual = interval ?? TimeSpan.FromMinutes(5);
        _timer.Change(TimeSpan.Zero, actual);
    }

    public bool ValidateCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return false;
        }

        lock (_gate)
        {
            // Enforce lockout period after too many failed attempts
            if (DateTime.UtcNow < _lockoutUntilUtc)
            {
                return false;
            }

            if (string.Equals(_currentCode, code.Trim(), StringComparison.Ordinal))
            {
                _consecutiveFailures = 0;
                return true;
            }

            // Track failure and enforce lockout after threshold
            _consecutiveFailures++;
            if (_consecutiveFailures >= MaxAttemptsBeforeLockout)
            {
                _lockoutUntilUtc = DateTime.UtcNow.AddSeconds(LockoutSeconds);
                Console.WriteLine($"[PairingService] Too many pairing failures ({_consecutiveFailures}), locked out for {LockoutSeconds}s.");
            }

            return false;
        }
    }

    private void Rotate()
    {
        lock (_gate)
        {
            _currentCode = GenerateCode();
            _lastRotatedUtc = DateTime.UtcNow;
            // Reset failure counter on code rotation so legitimate users aren't locked out forever
            _consecutiveFailures = 0;
            _lockoutUntilUtc = DateTime.MinValue;
        }
    }

    private static string GenerateCode()
    {
        // 6-digit numeric code
        var value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6");
    }

    public void Dispose() => _timer.Dispose();
}

