namespace ESP.DocumentExtractor.Domain.Exceptions;

public sealed class BlobStorageException(string message, Exception? innerException = null) : Exception(message, innerException);
