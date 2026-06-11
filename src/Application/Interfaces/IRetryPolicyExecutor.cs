using ESP.DocumentExtractor.Domain.ResultPattern;

namespace ESP.DocumentExtractor.Application.Interfaces;

public interface IRetryPolicyExecutor
{
    Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> operation, string operationName, CancellationToken cancellationToken);
    Task<Result> ExecuteAsync(Func<CancellationToken, Task<Result>> operation, string operationName, CancellationToken cancellationToken);
}
