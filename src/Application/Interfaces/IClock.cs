namespace ESP.DocumentExtractor.Application.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
