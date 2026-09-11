using Circle_Tracker.Services;
using FluentAssertions;
using Xunit;

namespace Circle_Tracker.Tests.ServiceTests;

public class CircuitBreakerTests
{
    [Fact]
    public void AllowRequest_InitialState_ReturnsTrue()
    {
        var breaker = new CircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(60));

        var result = breaker.AllowRequest();

        result.Should().BeTrue();
    }

    [Fact]
    public void AllowRequest_AfterThresholdFailures_ReturnsFalse()
    {
        var breaker = new CircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(60));

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();

        var result = breaker.AllowRequest();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task AllowRequest_AfterCooldown_ReturnsTrue()
    {
        var breaker = new CircuitBreaker(failureThreshold: 2, openDuration: TimeSpan.FromMilliseconds(50));

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.AllowRequest().Should().BeFalse();

        await Task.Delay(60);

        var result = breaker.AllowRequest();

        result.Should().BeTrue();
    }

    [Fact]
    public void RecordSuccess_ResetsFailureCount()
    {
        var breaker = new CircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(60));

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordSuccess();
        breaker.RecordFailure();

        var result = breaker.AllowRequest();

        result.Should().BeTrue();
        breaker.CurrentState.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task RecordSuccess_TransitionsFromHalfOpenToClosed()
    {
        var breaker = new CircuitBreaker(failureThreshold: 2, openDuration: TimeSpan.FromMilliseconds(50));

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.CurrentState.Should().Be(CircuitState.Open);

        await Task.Delay(60);
        breaker.AllowRequest().Should().BeTrue();
        breaker.CurrentState.Should().Be(CircuitState.HalfOpen);

        breaker.RecordSuccess();

        breaker.CurrentState.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void RecordFailure_BelowThreshold_StaysClosed()
    {
        var breaker = new CircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(60));

        breaker.RecordFailure();
        breaker.RecordFailure();

        breaker.CurrentState.Should().Be(CircuitState.Closed);
        breaker.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public void RecordFailure_AtThreshold_TransitionsToOpen()
    {
        var breaker = new CircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(60));

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();

        breaker.CurrentState.Should().Be(CircuitState.Open);
    }

    [Fact]
    public void CurrentState_InitialState_IsClosed()
    {
        var breaker = new CircuitBreaker(failureThreshold: 3, openDuration: TimeSpan.FromSeconds(60));

        breaker.CurrentState.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task AllowRequest_HalfOpen_AllowsOneProbe()
    {
        var breaker = new CircuitBreaker(failureThreshold: 2, openDuration: TimeSpan.FromMilliseconds(50));

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.CurrentState.Should().Be(CircuitState.Open);

        await Task.Delay(60);

        breaker.AllowRequest().Should().BeTrue();
        breaker.CurrentState.Should().Be(CircuitState.HalfOpen);
        breaker.AllowRequest().Should().BeFalse();
    }

    [Fact]
    public async Task Breaker_WhenHalfOpen_AllowsSingleProbe()
    {
        var breaker = new CircuitBreaker(failureThreshold: 2, openDuration: TimeSpan.FromMilliseconds(50));

        breaker.RecordFailure();
        breaker.RecordFailure();

        await Task.Delay(60);

        bool first = breaker.AllowRequest();
        bool second = breaker.AllowRequest();

        (first, second).Should().Be((true, false));
    }
}
