namespace ESP.DocumentExtractor.Domain.Exceptions;

public sealed class DatabaseException(string message, Exception? innerException = null) : Exception(message, innerException);
