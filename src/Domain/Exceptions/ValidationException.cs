namespace ESP.DocumentExtractor.Domain.Exceptions;

public sealed class ValidationException(string message) : Exception(message);
