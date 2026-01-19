using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EntglDb.Core.Exceptions;
using EntglDb.Core.Network;

namespace EntglDb.Core.Resilience;

/// <summary>
/// Provides retry logic for transient failures.
/// </summary>
public class RetryPolicy : IRetryPolicy
{
    private readonly IPeerNodeConfigurationProvider _peerNodeConfigurationProvider;
    private readonly ILogger<RetryPolicy> _logger;

    public RetryPolicy(IPeerNodeConfigurationProvider peerNodeConfigurationProvider, ILogger<RetryPolicy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _peerNodeConfigurationProvider = peerNodeConfigurationProvider
            ?? throw new ArgumentNullException(nameof(peerNodeConfigurationProvider));
    }

    /// <summary>
    /// Executes an operation with retry logic.
    /// </summary>
    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        var config = await _peerNodeConfigurationProvider.GetConfiguration();
        Exception? lastException = null;

        for (int attempt = 1; attempt <= config.RetryAttempts; attempt++)
        {
            try
            {
                _logger.LogDebug("Executing {Operation} (attempt {Attempt}/{Max})",
                    operationName, attempt, config.RetryAttempts);

                return await operation();
            }
            catch (Exception ex) when (attempt < config.RetryAttempts && IsTransient(ex))
            {
                lastException = ex;
                var delay = config.RetryDelayMs * attempt; // Exponential backoff

                _logger.LogWarning(ex,
                    "Operation {Operation} failed (attempt {Attempt}/{Max}). Retrying in {Delay}ms...",
                    operationName, attempt, config.RetryAttempts, delay);

                await Task.Delay(delay, cancellationToken);
            }
        }

        _logger.LogError(lastException,
            "Operation {Operation} failed after {Attempts} attempts",
            operationName, config.RetryAttempts);

        throw new EntglDbException("RETRY_EXHAUSTED",
            $"Operation '{operationName}' failed after {config.RetryAttempts} attempts",
            lastException!);
    }

    /// <summary>
    /// Executes an operation with retry logic (void return).
    /// </summary>
    public async Task ExecuteAsync(
        Func<Task> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(async () =>
        {
            await operation();
            return true;
        }, operationName, cancellationToken);
    }

    private static bool IsTransient(Exception ex)
    {
        // Network errors are typically transient
        if (ex is NetworkException or System.Net.Sockets.SocketException or System.IO.IOException)
            return true;

        // Timeout errors are transient
        if (ex is Exceptions.TimeoutException or OperationCanceledException)
            return true;

        return false;
    }
}
