namespace ESP.DocumentExtractor.Domain.Exceptions;

public sealed class ExtractionException(string message, Exception? innerException = null) : Exception(message, innerException);
