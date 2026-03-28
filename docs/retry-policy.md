# RetryPolicy

## Problem

Transient failures are common in distributed systems — network timeouts, rate limits, temporary
service unavailability. `RetryPolicy` provides a simple, composable way to retry any async
operation without coupling retry logic to your application code.

## API Overview

All methods are static members of `ResilienceToolkit.Retry.RetryPolicy`.

### Retry on any exception

```csharp
// Value-returning operation
Task<TResult> RetryAsync<TResult>(
    Func<Task<TResult>> operation,
    int retries,
    TimeSpan retryDelay,                  // fixed delay overload
    CancellationToken cancellationToken = default,
    Action<Exception, int>? notifyException = null)

Task<TResult> RetryAsync<TResult>(
    Func<Task<TResult>> operation,
    int retries,
    RetryDelayStrategy delayStrategy,     // strategy overload
    CancellationToken cancellationToken = default,
    Action<Exception, int>? notifyException = null)

// Void operation
Task RetryAsync(Func<Task> operation, int retries, TimeSpan retryDelay, ...)
Task RetryAsync(Func<Task> operation, int retries, RetryDelayStrategy delayStrategy, ...)
```

### Retry on a specific exception type

```csharp
Task<TResult> RetryOnExceptionAsync<TResult, TException>(
    Func<Task<TResult>> operation,
    int retries,
    TimeSpan retryDelay,
    CancellationToken cancellationToken = default,
    Action<Exception, int>? notifyException = null)
    where TException : Exception

Task RetryOnExceptionAsync<TException>(
    Func<Task> operation,
    int retries,
    RetryDelayStrategy delayStrategy,
    CancellationToken cancellationToken = default,
    Action<Exception, int>? notifyException = null)
    where TException : Exception
```

## Retries vs Attempts

The `retries` parameter represents the **total number of attempts**, including the initial one.
It does not represent the number of extra retries after the first attempt.

| `retries` value | Attempts made |
|-----------------|---------------|
| 1               | 1 (no retry)  |
| 3               | up to 3       |
| 5               | up to 5       |

## Exception Filtering

`RetryOnExceptionAsync<TException>` only retries exceptions that are assignable to `TException`.
Any other exception type propagates immediately without delay.

`OperationCanceledException` is **never** retried, regardless of `TException`. If the operation
throws a cancellation exception, it is rethrown immediately.

## Cancellation

`CancellationToken` is observed at two points:

1. **Before each attempt** — `ThrowIfCancellationRequested()` is called at the top of every
   iteration, so a pre-cancelled token is detected before the very first attempt.
2. **During each delay** — the token is passed to `Task.Delay`, so cancellation during a wait
   is also prompt.

When cancellation occurs, `OperationCanceledException` is rethrown as-is (never wrapped).

## The `notifyException` Callback

The optional `notifyException: (ex, attempt) => ...` callback is invoked on every retryable
exception, including the final failed attempt. This makes it suitable for structured logging:

```csharp
notifyException: (ex, attempt) =>
    logger.LogWarning(ex, "Attempt {Attempt} failed", attempt)
```

Exceptions thrown inside `notifyException` are not swallowed and will terminate the retry loop.
