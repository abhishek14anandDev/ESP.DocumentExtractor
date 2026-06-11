namespace ESP.DocumentExtractor.Domain.Enums;

public enum ProcessingStatus
{
    Received = 1,
    Classified = 2,
    Extracted = 3,
    Validated = 4,
    Persisted = 5,
    Processed = 6,
    Rejected = 7,
    Failed = 8
}
