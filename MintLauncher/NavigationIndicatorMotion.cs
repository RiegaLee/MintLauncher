namespace MintLauncher;

/// <summary>Interruptible position interpolation for the top navigation highlight.</summary>
internal sealed class NavigationIndicatorMotion
{
    private double _from;
    private double _to;
    private double _startedAt;
    private double _duration;
    private bool _initialized;

    public bool IsInitialized => _initialized;
    public double Target => _to;

    public void SetInstant(double position)
    {
        _from = _to = position;
        _initialized = true;
        _duration = 0;
    }

    public void Retarget(double position, double nowMilliseconds, double durationMilliseconds)
    {
        var current = ValueAt(nowMilliseconds);
        _from = current;
        _to = position;
        _startedAt = nowMilliseconds;
        _duration = Math.Abs(position - current) < 0.5 ? 0 : durationMilliseconds;
        _initialized = true;
    }

    public double ValueAt(double nowMilliseconds)
    {
        if (!_initialized || _duration <= 0) return _to;
        var progress = Math.Clamp((nowMilliseconds - _startedAt) / _duration, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        return _from + (_to - _from) * eased;
    }

    public bool IsCompleteAt(double nowMilliseconds) =>
        !_initialized || _duration <= 0 || nowMilliseconds - _startedAt >= _duration;
}
