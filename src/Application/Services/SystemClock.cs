using ESP.DocumentExtractor.Application.Interfaces;

namespace ESP.DocumentExtractor.Application.Services;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
