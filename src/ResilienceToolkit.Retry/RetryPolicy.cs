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
    /// <typeparam name="TResult">The type of the value returned by the operation.</typeparam>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="retries">
    /// The maximum number of attempts, including the initial attempt.
    /// Must be greater than zero.
    /// </param>
    /// <param name="retryDelay">
    /// The constant delay to wait between failed attempts.
    /// Must be greater than or equal to <see cref="TimeSpan.Zero"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the retry operation.
    /// Cancellation is observed before each attempt and during each delay.
    /// </param>
    /// <param name="notifyException">
    /// An optional callback invoked whenever a retryable exception is observed.
    /// The callback receives the exception and the 1-based attempt number that failed.
    /// The callback is also invoked on the final failed attempt.
    /// </param>
    /// <returns>
    /// A task that completes with the value returned by <paramref name="operation"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="retries"/> is less than or equal to zero,
    /// or when <paramref name="retryDelay"/> is negative.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the retry operation is cancelled.
    /// </exception>
    public static Task<TResult> RetryAsync<TResult>(
        Func<Task<TResult>> operation,
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
    /// <typeparam name="TResult">The type of the value returned by the operation.</typeparam>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="retries">
    /// The maximum number of attempts, including the initial attempt.
    /// Must be greater than zero.
    /// </param>
    /// <param name="delayStrategy">
    /// A strategy that calculates the delay to wait before the next attempt,
    /// based on the failed attempt number and the caught exception.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the retry operation.
    /// Cancellation is observed before each attempt and during each delay.
    /// </param>
    /// <param name="notifyException">
    /// An optional callback invoked whenever a retryable exception is observed.
    /// The callback receives the exception and the 1-based attempt number that failed.
    /// The callback is also invoked on the final failed attempt.
    /// </param>
    /// <returns>
    /// A task that completes with the value returned by <paramref name="operation"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> or <paramref name="delayStrategy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="retries"/> is less than or equal to zero.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="delayStrategy"/> returns a negative delay.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the retry operation is cancelled.
    /// </exception>
    /// <remarks>
    /// Exceptions thrown by <paramref name="notifyException"/> or <paramref name="delayStrategy"/>
    /// are not swallowed and will terminate the retry operation.
    /// </remarks>
    public static async Task<TResult> RetryAsync<TResult>(
        Func<Task<TResult>> operation,
        int retries,
        RetryDelayStrategy delayStrategy,
        CancellationToken cancellationToken = default,
        Action<Exception, int>? notifyException = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(delayStrategy);
        ValidateRetryCount(retries);

        for (int attempt = 1; attempt <= retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
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

    /// <summary>
    /// Executes an asynchronous operation and retries it with a constant delay when it fails.
    /// </summary>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="retries">
    /// The maximum number of attempts, including the initial attempt.
    /// Must be greater than zero.
    /// </param>
    /// <param name="retryDelay">
    /// The constant delay to wait between failed attempts.
    /// Must be greater than or equal to <see cref="TimeSpan.Zero"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the retry operation.
    /// Cancellation is observed before each attempt and during each delay.
    /// </param>
    /// <param name="notifyException">
    /// An optional callback invoked whenever a retryable exception is observed.
    /// The callback receives the exception and the 1-based attempt number that failed.
    /// The callback is also invoked on the final failed attempt.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous retry operation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="retries"/> is less than or equal to zero,
    /// or when <paramref name="retryDelay"/> is negative.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the retry operation is cancelled.
    /// </exception>
    public static Task RetryAsync(
        Func<Task> operation,
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
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="retries">
    /// The maximum number of attempts, including the initial attempt.
    /// Must be greater than zero.
    /// </param>
    /// <param name="delayStrategy">
    /// A strategy that calculates the delay to wait before the next attempt,
    /// based on the failed attempt number and the caught exception.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the retry operation.
    /// Cancellation is observed before each attempt and during each delay.
    /// </param>
    /// <param name="notifyException">
    /// An optional callback invoked whenever a retryable exception is observed.
    /// The callback receives the exception and the 1-based attempt number that failed.
    /// The callback is also invoked on the final failed attempt.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous retry operation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> or <paramref name="delayStrategy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="retries"/> is less than or equal to zero.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="delayStrategy"/> returns a negative delay.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the retry operation is cancelled.
    /// </exception>
    /// <remarks>
    /// Exceptions thrown by <paramref name="notifyException"/> or <paramref name="delayStrategy"/>
    /// are not swallowed and will terminate the retry operation.
    /// </remarks>
    public static async Task RetryAsync(
        Func<Task> operation,
        int retries,
        RetryDelayStrategy delayStrategy,
        CancellationToken cancellationToken = default,
        Action<Exception, int>? notifyException = null)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(delayStrategy);
        ValidateRetryCount(retries);

        for (int attempt = 1; attempt <= retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await operation().ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
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
    }

    /// <summary>
    /// Executes an asynchronous operation and retries it with a constant delay only when the thrown exception
    /// matches <typeparamref name="TException"/>.
    /// </summary>
    /// <typeparam name="TResult">The type of the value returned by the operation.</typeparam>
    /// <typeparam name="TException">
    /// The exception type that is considered retryable.
    /// Any other exception type is rethrown immediately.
    /// </typeparam>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="retries">
    /// The maximum number of attempts, including the initial attempt.
    /// Must be greater than zero.
    /// </param>
    /// <param name="retryDelay">
    /// The constant delay to wait between failed attempts.
    /// Must be greater than or equal to <see cref="TimeSpan.Zero"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the retry operation.
    /// Cancellation is observed before each attempt and during each delay.
    /// </param>
    /// <param name="notifyException">
    /// An optional callback invoked whenever a retryable exception is observed.
    /// The callback receives the exception and the 1-based attempt number that failed.
    /// The callback is also invoked on the final failed attempt.
    /// </param>
    /// <returns>
    /// A task that completes with the value returned by <paramref name="operation"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="retries"/> is less than or equal to zero,
    /// or when <paramref name="retryDelay"/> is negative.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the retry operation is cancelled.
    /// </exception>
    public static Task<TResult> RetryOnExceptionAsync<TResult, TException>(
        Func<Task<TResult>> operation,
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
    /// <typeparam name="TResult">The type of the value returned by the operation.</typeparam>
    /// <typeparam name="TException">
    /// The exception type that is considered retryable.
    /// Any other exception type is rethrown immediately.
    /// </typeparam>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="retries">
    /// The maximum number of attempts, including the initial attempt.
    /// Must be greater than zero.
    /// </param>
    /// <param name="delayStrategy">
    /// A strategy that calculates the delay to wait before the next attempt,
    /// based on the failed attempt number and the caught exception.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the retry operation.
    /// Cancellation is observed before each attempt and during each delay.
    /// </param>
    /// <param name="notifyException">
    /// An optional callback invoked whenever a retryable exception is observed.
    /// The callback receives the exception and the 1-based attempt number that failed.
    /// The callback is also invoked on the final failed attempt.
    /// </param>
    /// <returns>
    /// A task that completes with the value returned by <paramref name="operation"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> or <paramref name="delayStrategy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="retries"/> is less than or equal to zero.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="delayStrategy"/> returns a negative delay.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the retry operation is cancelled.
    /// </exception>
    /// <remarks>
    /// Only exceptions assignable to <typeparamref name="TException"/> are retried.
    /// <see cref="OperationCanceledException"/> is never retried, even when it is assignable
    /// to <typeparamref name="TException"/>.
    /// </remarks>
    public static async Task<TResult> RetryOnExceptionAsync<TResult, TException>(
        Func<Task<TResult>> operation,
        int retries,
        RetryDelayStrategy delayStrategy,
        CancellationToken cancellationToken = default,
        Action<Exception, int>? notifyException = null)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(delayStrategy);
        ValidateRetryCount(retries);

        for (int attempt = 1; attempt <= retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    throw;
                }

                if (ex is not TException)
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

    /// <summary>
    /// Executes an asynchronous operation and retries it with a constant delay only when the thrown exception
    /// matches <typeparamref name="TException"/>.
    /// </summary>
    /// <typeparam name="TException">
    /// The exception type that is considered retryable.
    /// Any other exception type is rethrown immediately.
    /// </typeparam>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="retries">
    /// The maximum number of attempts, including the initial attempt.
    /// Must be greater than zero.
    /// </param>
    /// <param name="retryDelay">
    /// The constant delay to wait between failed attempts.
    /// Must be greater than or equal to <see cref="TimeSpan.Zero"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the retry operation.
    /// Cancellation is observed before each attempt and during each delay.
    /// </param>
    /// <param name="notifyException">
    /// An optional callback invoked whenever a retryable exception is observed.
    /// The callback receives the exception and the 1-based attempt number that failed.
    /// The callback is also invoked on the final failed attempt.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous retry operation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="retries"/> is less than or equal to zero,
    /// or when <paramref name="retryDelay"/> is negative.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the retry operation is cancelled.
    /// </exception>
    public static Task RetryOnExceptionAsync<TException>(
        Func<Task> operation,
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
    /// <typeparam name="TException">
    /// The exception type that is considered retryable.
    /// Any other exception type is rethrown immediately.
    /// </typeparam>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="retries">
    /// The maximum number of attempts, including the initial attempt.
    /// Must be greater than zero.
    /// </param>
    /// <param name="delayStrategy">
    /// A strategy that calculates the delay to wait before the next attempt,
    /// based on the failed attempt number and the caught exception.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the retry operation.
    /// Cancellation is observed before each attempt and during each delay.
    /// </param>
    /// <param name="notifyException">
    /// An optional callback invoked whenever a retryable exception is observed.
    /// The callback receives the exception and the 1-based attempt number that failed.
    /// The callback is also invoked on the final failed attempt.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous retry operation.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="operation"/> or <paramref name="delayStrategy"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="retries"/> is less than or equal to zero.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="delayStrategy"/> returns a negative delay.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when the retry operation is cancelled.
    /// </exception>
    /// <remarks>
    /// Only exceptions assignable to <typeparamref name="TException"/> are retried.
    /// <see cref="OperationCanceledException"/> is never retried, even when it is assignable
    /// to <typeparamref name="TException"/>.
    /// </remarks>
    public static async Task RetryOnExceptionAsync<TException>(Func<Task> operation, int retries, RetryDelayStrategy delayStrategy,
        CancellationToken cancellationToken = default, Action<Exception, int>? notifyException = null)
        where TException : Exception
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(delayStrategy);
        ValidateRetryCount(retries);

        for (int attempt = 1; attempt <= retries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await operation().ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException)
                {
                    throw;
                }

                if (ex is not TException)
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
    }

    private static void ValidateRetryCount(int retries)
    {
        if (retries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(retries), retries, "The number of attempts must be greater than zero.");
        }
    }

    private static void ValidateRetryDelay(TimeSpan retryDelay, string paramName)
    {
        if (retryDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(paramName, retryDelay,"The retry delay must be greater than or equal to zero.");
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
}