using System;

namespace Circle_Tracker.Services;

public enum CircuitState
{
    Closed,
    Open,
    HalfOpen
}

public class CircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly TimeSpan _openDuration;
    private readonly object _lock = new();

    private int _failureCount;
    private CircuitState _state = CircuitState.Closed;
    private DateTime _lastFailureTime = DateTime.MinValue;

    public CircuitBreaker(int failureThreshold, TimeSpan openDuration)
    {
        _failureThreshold = failureThreshold;
        _openDuration = openDuration;
    }

    public CircuitState CurrentState
    {
        get
        {
            lock (_lock)
            {
                return _state;
            }
        }
    }

    public bool AllowRequest()
    {
        lock (_lock)
        {
            if (_state == CircuitState.Closed)
            {
                return true;
            }

            if (_state == CircuitState.Open)
            {
                var timeSinceFailure = DateTime.UtcNow - _lastFailureTime;
                if (timeSinceFailure >= _openDuration)
                {
                    _state = CircuitState.HalfOpen;
                    return true;
                }
                return false;
            }

            return true;
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            _failureCount = 0;
            _state = CircuitState.Closed;
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;

            if (_failureCount >= _failureThreshold)
            {
                _state = CircuitState.Open;
            }
        }
    }
}
