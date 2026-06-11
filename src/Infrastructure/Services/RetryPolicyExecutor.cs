using ESP.DocumentExtractor.Application.Interfaces;
using ESP.DocumentExtractor.Domain.ResultPattern;
using ESP.DocumentExtractor.Infrastructure.Configurations;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ESP.DocumentExtractor.Infrastructure.Services;

public sealed class RetryPolicyExecutor(
    IOptions<RetryOptions> options,
    ILogger<RetryPolicyExecutor> logger) : IRetryPolicyExecutor
{
    public async Task<Result<T>> ExecuteAsync<T>(Func<CancellationToken, Task<Result<T>>> operation, string operationName, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= options.Value.MaxAttempts; attempt++)
        {
            try
            {
                var result = await operation(cancellationToken);
                if (result.IsSuccess || attempt == options.Value.MaxAttempts)
                {
                    return result;
                }
            }
            catch (Exception ex) when (attempt < options.Value.MaxAttempts)
            {
                logger.LogWarning(ex, "Transient error in {OperationName} on attempt {Attempt}.", operationName, attempt);
            }

            await DelayAsync(attempt, cancellationToken);
        }

        return Result<T>.Failure(new Error("retry.exhausted", $"Operation '{operationName}' failed after retry exhaustion."));
    }

    public async Task<Result> ExecuteAsync(Func<CancellationToken, Task<Result>> operation, string operationName, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= options.Value.MaxAttempts; attempt++)
        {
            try
            {
                var result = await operation(cancellationToken);
                if (result.IsSuccess || attempt == options.Value.MaxAttempts)
                {
                    return result;
                }
            }
            catch (Exception ex) when (attempt < options.Value.MaxAttempts)
            {
                logger.LogWarning(ex, "Transient error in {OperationName} on attempt {Attempt}.", operationName, attempt);
            }

            await DelayAsync(attempt, cancellationToken);
        }

        return Result.Failure(new Error("retry.exhausted", $"Operation '{operationName}' failed after retry exhaustion."));
    }

    private Task DelayAsync(int attempt, CancellationToken cancellationToken)
    {
        var exponent = Math.Pow(options.Value.BackoffMultiplier, attempt - 1);
        var delay = TimeSpan.FromMilliseconds(options.Value.BaseDelayMilliseconds * exponent);
        return Task.Delay(delay, cancellationToken);
    }
}
