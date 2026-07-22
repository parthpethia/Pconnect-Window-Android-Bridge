using System;
using System.Threading;
using System.Threading.Tasks;
using Pconnect.Agent.Resilience;
using Xunit;

namespace Pconnect.Agent.Tests;

public class ResilienceTests
{
    [Fact]
    public void ConsecutiveCircuitBreaker_TripsAfterThresholdFailures()
    {
        var breaker = new ConsecutiveCircuitBreaker(threshold: 3, halfOpenTimeout: TimeSpan.FromSeconds(2));

        Assert.False(breaker.IsOpen);
        breaker.RecordFailure();
        Assert.False(breaker.IsOpen);
        breaker.RecordFailure();
        Assert.False(breaker.IsOpen);

        breaker.RecordFailure();
        Assert.True(breaker.IsOpen);
    }

    [Fact]
    public void ConsecutiveCircuitBreaker_ResetsOnSuccess()
    {
        var breaker = new ConsecutiveCircuitBreaker(threshold: 2);
        breaker.RecordFailure();
        breaker.RecordSuccess();
        breaker.RecordFailure();

        Assert.False(breaker.IsOpen);
    }

    [Fact]
    public void TimeWindowedCircuitBreaker_TripsOnlyWhenFailuresExceedWindowThreshold()
    {
        var breaker = new TimeWindowedCircuitBreaker(thresholdCount: 3, timeWindow: TimeSpan.FromSeconds(5), halfOpenTimeout: TimeSpan.FromSeconds(5));

        Assert.False(breaker.IsOpen);
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.False(breaker.IsOpen);

        breaker.RecordFailure();
        Assert.True(breaker.IsOpen);
    }

    [Fact]
    public void BufferPool_RentsAndReturnsArray()
    {
        var buffer = BufferPool.Rent(1024);
        Assert.NotNull(buffer);
        Assert.True(buffer.Length >= 1024);

        BufferPool.Return(buffer);
    }

    [Fact]
    public void BoundedCancellationTokenSource_TimesOutAsExpected()
    {
        using var bounded = new BoundedCancellationTokenSource(TimeSpan.FromMilliseconds(50));
        Assert.False(bounded.Token.IsCancellationRequested);

        Thread.Sleep(100);
        Assert.True(bounded.Token.IsCancellationRequested);
    }
}
