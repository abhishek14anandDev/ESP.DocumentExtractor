namespace ESP.DocumentExtractor.Domain.Exceptions;

public sealed class DocumentProcessingException(string message, Exception? innerException = null) : Exception(message, innerException);
