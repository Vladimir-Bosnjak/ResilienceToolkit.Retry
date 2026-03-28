namespace ResilienceToolkit.Retry;

/// <summary>
/// Represents a strategy that calculates the delay before the next retry attempt.
/// </summary>
/// <param name="attemptNumber">
/// The 1-based number of the failed attempt that triggered the retry.
/// For example, a value of <c>1</c> means the first attempt failed and the returned delay
/// will be used before the second attempt.
/// </param>
/// <param name="exception">
/// The retryable exception that was thrown by the failed attempt.
/// </param>
/// <returns>
/// The delay to wait before the next attempt.
/// The returned value must be greater than or equal to <see cref="TimeSpan.Zero"/>.
/// </returns>
public delegate TimeSpan RetryDelayStrategy(int attemptNumber, Exception exception);
