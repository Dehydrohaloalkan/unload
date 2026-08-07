namespace Unload.Backend.Tests;

internal sealed class ManualTimeProvider(DateTimeOffset currentUtc, TimeZoneInfo? localTimeZone = null)
    : TimeProvider
{
    private DateTimeOffset _currentUtc = currentUtc.ToUniversalTime();

    public override TimeZoneInfo LocalTimeZone { get; } = localTimeZone ?? TimeZoneInfo.Utc;

    public override DateTimeOffset GetUtcNow()
    {
        return _currentUtc;
    }

    public void SetUtcNow(DateTimeOffset value)
    {
        _currentUtc = value.ToUniversalTime();
    }
}
