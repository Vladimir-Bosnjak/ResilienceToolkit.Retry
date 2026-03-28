using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ResilienceToolkit.Retry;

/// <summary>
/// Provides helper methods for retrying asynchronous operations.
/// </summary>
/// <remarks>
/// The <paramref name="retries"/> parameter on all overloads represents the total number of attempts,
/// including the initial attempt.
/// <para>
/// Cancellation is never retried. If an <see cref="OperationCanceledException"/> is observed,
/// it is rethrown immediately.
/// </para>
/// </remarks>
public static class RetryPolicy
{
    /// <summary>
    /// Executes an asynchronous operation and retries it with a constant delay when it fails.
    /// </summary>
    public static Task<TResult> RetryAsync<TResult>(Func<Task<TResult>> operation,
        int retries,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default,
        Action<Exception, int>? notifyException = null)
    {
        ValidateRetryDelay(retryDelay, nameof(retryDelay));

        return RetryAsync(
            operation,
            retries,
            (_, _) => retryDelay,
            cancellationToken,
            notifyException);
    }

    /// <summary>
    /// Executes an asynchronous operation and retries it using a caller-provided delay strategy.
    /// </summary>
    /// <remarks>
    /// Exceptions thrown by <paramref name="notifyException"/> or <paramref name="delayStrategy"/>
    /// are not swallowed and will terminate the retry operation.
    /// </remarks>
    public static Task<TResult> RetryAsync<TResult>(Func<Task<TResult>> operation,
        int retries,
        RetryDelayStrategy delayStrategy,
        CancellationToken cancellationToken = default,
        Action<Exception, int>? notifyException = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteRetryAsync(
            operation,
            retries,
            delayStrategy,
            static _ => true,
            cancellationToken,
            notifyException);
    }

    /// <summary>
    /// Executes an asynchronous operation and retries it with a constant delay when it fails.
    /// </summary>
    public static Task RetryAsync(Func<Task> operation,
        int retries,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default,
        Action<Exception, int>? notifyException = null)
    {
        ValidateRetryDelay(retryDelay, nameof(retryDelay));

        return RetryAsync(
            operation,
            retries,
            (_, _) => retryDelay,
            cancellationToken,
            notifyException);
    }

    /// <summary>
    /// Executes an asynchronous operation and retries it using a caller-provided delay strategy.
    /// </summary>
    /// <remarks>
    /// Exceptions thrown by <paramref name="notifyException"/> or <paramref name="delayStrategy"/>
    /// are not swallowed and will terminate the retry operation.
    /// </remarks>
    public static async Task RetryAsync(Func<Task> operation,
        int retries,
        RetryDelayStrategy delayStrategy,
        CancellationToken cancellationToken = default,
        Action<Exception, int>? notifyException = null)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await ExecuteRetryAsync(
            async () =>
            {
                await operation().ConfigureAwait(false);
                return NoResult.Value;
            },
            retries,
            delayStrategy,
            static _ => true,
            cancellationToken,
            notifyException).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes an asynchronous operation and retries it with a constant delay only when the thrown exception
    /// matches <typeparamref name="TException"/>.
    /// </summary>
    public static Task<TResult> RetryOnExceptionAsync<TResult, TException>(Func<Task<TResult>> operation,
        int retries,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default,
        Action<Exception, int>? notifyException = null)
        where TException : Exception
    {
        ValidateRetryDelay(retryDelay, nameof(retryDelay));

        return RetryOnExceptionAsync<TResult, TException>(
            operation,
            retries,
            (_, _) => retryDelay,
            cancellationToken,
            notifyException);
    }

    /// <summary>
    /// Executes an asynchronous operation and retries it using a caller-provided delay strategy
    /// only when the thrown exception matches <typeparamref name="TException"/>.
    /// </summary>
    /// <remarks>
    /// Only exceptions assignable to <typeparamref name="TException"/> are retried.
    /// <see cref="OperationCanceledException"/> is never retried, even when it is assignable
    /// to <typeparamref name="TException"/>.
    /// </remarks>
    public static Task<TResult> RetryOnExceptionAsync<TResult, TException>(Func<Task<TResult>> operation,
        int retries,
        RetryDelayStrategy delayStrategy,
        CancellationToken cancellationToken = default,
        Action<Exception, int>? notifyException = null)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(operation);

        return ExecuteRetryAsync(
            operation,
            retries,
            delayStrategy,
            static ex => ex is TException,
            cancellationToken,
            notifyException);
    }

    /// <summary>
    /// Executes an asynchronous operation and retries it with a constant delay only when the thrown exception
    /// matches <typeparamref name="TException"/>.
    /// </summary>
    public static Task RetryOnExceptionAsync<TException>(Func<Task> operation,
        int retries,
        TimeSpan retryDelay,
        CancellationToken cancellationToken = default,
        Action<Exception, int>? notifyException = null)
        where TException : Exception
    {
        ValidateRetryDelay(retryDelay, nameof(retryDelay));

        return RetryOnExceptionAsync<TException>(
            operation,
            retries,
            (_, _) => retryDelay,
            cancellationToken,
            notifyException);
    }

    /// <summary>
    /// Executes an asynchronous operation and retries it using a caller-provided delay strategy
    /// only when the thrown exception matches <typeparamref name="TException"/>.
    /// </summary>
    /// <remarks>
    /// Only exceptions assignable to <typeparamref name="TException"/> are retried.
    /// <see cref="OperationCanceledException"/> is never retried, even when it is assignable
    /// to <typeparamref name="TException"/>.
    /// </remarks>
    public static async Task RetryOnExceptionAsync<TException>(Func<Task> operation,
        int retries,
        RetryDelayStrategy delayStrategy,
        CancellationToken cancellationToken = default,
        Action<Exception, int>? notifyException = null)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(operation);

        await ExecuteRetryAsync(
            async () =>
            {
                await operation().ConfigureAwait(false);
                return NoResult.Value;
            },
            retries,
            delayStrategy,
            static ex => ex is TException,
            cancellationToken,
            notifyException).ConfigureAwait(false);
    }

    private static async Task<TResult> ExecuteRetryAsync<TResult>(Func<Task<TResult>> operation,
        int retries,
        RetryDelayStrategy delayStrategy,
        Func<Exception, bool> shouldRetry,
        CancellationToken cancellationToken,
        Action<Exception, int>? notifyException)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(delayStrategy);
        ArgumentNullException.ThrowIfNull(shouldRetry);

        ValidateRetryCount(retries);

        for (int attempt = 1; attempt <= retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!shouldRetry(ex))
                {
                    throw;
                }

                notifyException?.Invoke(ex, attempt);

                if (attempt == retries)
                {
                    throw;
                }

                TimeSpan delay = GetDelay(delayStrategy, attempt, ex);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        throw new UnreachableException();
    }

    private static void ValidateRetryCount(int retries)
    {
        if (retries <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retries),
                retries,
                "The number of attempts must be greater than zero.");
        }
    }

    private static void ValidateRetryDelay(TimeSpan retryDelay, string paramName)
    {
        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                retryDelay,
                "The retry delay must be greater than or equal to zero.");
        }
    }

    private static TimeSpan GetDelay(RetryDelayStrategy delayStrategy, int attemptNumber, Exception exception)
    {
        TimeSpan delay = delayStrategy(attemptNumber, exception);

        if (delay < TimeSpan.Zero)
        {
            throw new InvalidOperationException("The retry delay strategy returned a negative delay.");
        }

        return delay;
    }

    private readonly record struct NoResult
    {
        public static NoResult Value { get; } = new();
    }
}