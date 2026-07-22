using System.Buffers;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Pconnect.Agent.Resilience;

public sealed class BoundedCancellationTokenSource : IDisposable
{
    private readonly CancellationTokenSource _cts;

    public BoundedCancellationTokenSource(TimeSpan timeout, CancellationToken parentToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(parentToken);
        _cts.CancelAfter(timeout);
    }

    public CancellationToken Token => _cts.Token;

    public void Dispose() => _cts.Dispose();
}

public sealed class ConsecutiveCircuitBreaker
{
    private readonly int _threshold;
    private readonly TimeSpan _halfOpenTimeout;
    private int _consecutiveFailures;
    private DateTimeOffset _lastFailureTime = DateTimeOffset.MinValue;
    private bool _isOpen;
    private readonly object _lock = new();

    public ConsecutiveCircuitBreaker(int threshold = 3, TimeSpan? halfOpenTimeout = null)
    {
        _threshold = threshold;
        _halfOpenTimeout = halfOpenTimeout ?? TimeSpan.FromSeconds(5);
    }

    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                if (!_isOpen) return false;
                if (DateTimeOffset.UtcNow - _lastFailureTime >= _halfOpenTimeout)
                {
                    // Transition to half-open
                    return false;
                }
                return true;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            _isOpen = false;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            _lastFailureTime = DateTimeOffset.UtcNow;
            if (_consecutiveFailures >= _threshold)
            {
                _isOpen = true;
            }
        }
    }

    public T Execute<T>(Func<T> action, T fallbackValue)
    {
        if (IsOpen) return fallbackValue;
        try
        {
            var result = action();
            RecordSuccess();
            return result;
        }
        catch
        {
            RecordFailure();
            return fallbackValue;
        }
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> actionAsync, T fallbackValue, CancellationToken ct)
    {
        if (IsOpen) return fallbackValue;
        try
        {
            var result = await actionAsync(ct).ConfigureAwait(false);
            RecordSuccess();
            return result;
        }
        catch
        {
            RecordFailure();
            return fallbackValue;
        }
    }
}

public sealed class TimeWindowedCircuitBreaker
{
    private readonly int _thresholdCount;
    private readonly TimeSpan _timeWindow;
    private readonly TimeSpan _halfOpenTimeout;
    private readonly Queue<DateTimeOffset> _failureTimestamps = new();
    private DateTimeOffset _lastTripTime = DateTimeOffset.MinValue;
    private bool _isOpen;
    private readonly object _lock = new();

    public TimeWindowedCircuitBreaker(int thresholdCount = 5, TimeSpan? timeWindow = null, TimeSpan? halfOpenTimeout = null)
    {
        _thresholdCount = thresholdCount;
        _timeWindow = timeWindow ?? TimeSpan.FromSeconds(10);
        _halfOpenTimeout = halfOpenTimeout ?? TimeSpan.FromSeconds(10);
    }

    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                if (!_isOpen) return false;
                if (DateTimeOffset.UtcNow - _lastTripTime >= _halfOpenTimeout)
                {
                    return false; // Half-open retry
                }
                return true;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failureTimestamps.Clear();
            _isOpen = false;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            _failureTimestamps.Enqueue(now);
            
            // Evict timestamps outside the window
            while (_failureTimestamps.Count > 0 && now - _failureTimestamps.Peek() > _timeWindow)
            {
                _failureTimestamps.Dequeue();
            }

            if (_failureTimestamps.Count >= _thresholdCount)
            {
                _isOpen = true;
                _lastTripTime = now;
            }
        }
    }
}

public static class BufferPool
{
    public static byte[] Rent(int minimumLength)
    {
        return ArrayPool<byte>.Shared.Rent(minimumLength);
    }

    public static void Return(byte[] array, bool clearArray = false)
    {
        ArrayPool<byte>.Shared.Return(array, clearArray);
    }
}

public sealed class StructuredErrorEnvelope
{
    [JsonPropertyName("v")]
    public int ProtocolVersion { get; set; } = 1;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "error";

    [JsonPropertyName("code")]
    public string Code { get; set; } = "INTERNAL_ERROR";

    [JsonPropertyName("severity")]
    public string Severity { get; set; } = "error"; // info, warning, error, critical

    [JsonPropertyName("retryable")]
    public bool Retryable { get; set; }

    [JsonPropertyName("userMessage")]
    public string UserMessage { get; set; } = string.Empty;

    public static StructuredErrorEnvelope Mismatch(string message) => new()
    {
        Code = "PROTOCOL_MISMATCH",
        Severity = "critical",
        Retryable = false,
        UserMessage = message
    };

    public static StructuredErrorEnvelope Unauthorized(string message) => new()
    {
        Code = "UNAUTHORIZED",
        Severity = "error",
        Retryable = true,
        UserMessage = message
    };

    public static StructuredErrorEnvelope RateLimited(string message) => new()
    {
        Code = "RATE_LIMITED",
        Severity = "warning",
        Retryable = true,
        UserMessage = message
    };
}
