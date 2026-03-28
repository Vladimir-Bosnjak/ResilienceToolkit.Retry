# Delay Strategies

## What Is a Delay Strategy?

`RetryDelayStrategy` is a delegate defined in `ResilienceToolkit.Retry`:

```csharp
public delegate TimeSpan RetryDelayStrategy(int attemptNumber, Exception exception);
```

It receives:

- `attemptNumber` — the **1-based** index of the attempt that just failed.
  A value of `1` means the first attempt failed; the returned delay will be waited before attempt 2.
- `exception` — the exception thrown by the failed attempt.

It must return a `TimeSpan >= TimeSpan.Zero`. Returning a negative value throws
`InvalidOperationException`.

## Why the Caller Controls It

Delay strategy is deliberately left to the caller. Different operations have very different
tolerance for retry frequency — a read from a local cache and a call to a rate-limited external
API should not share the same policy. Keeping the strategy as a simple delegate means:

- The library stays small and has no opinions about timings.
- The caller gets full control with minimal boilerplate.
- Any custom logic (caps, jitter, exception-dependent delays) can be expressed inline.

## Examples

### Constant Delay

Wait the same amount of time between every retry:

```csharp
RetryDelayStrategy constant = (_, _) => TimeSpan.FromSeconds(2);
```

### Exponential Backoff

Double the wait time on each failure, capped at 8 seconds:

```csharp
RetryDelayStrategy exponentialBackoff = static (attempt, _) =>
    TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 8));
```

| Attempt that failed | Delay before next attempt |
|---------------------|---------------------------|
| 1                   | 1 s                       |
| 2                   | 2 s                       |
| 3                   | 4 s                       |
| 4                   | 8 s                       |
| 5+                  | 8 s (capped)              |

This is the strategy used in the [`samples/RetryDemo`](../samples/RetryDemo) project.

### Exception-Aware Strategy

Inspect the exception to decide the delay — for example, respect a `Retry-After` header:

```csharp
RetryDelayStrategy adaptive = (attempt, ex) =>
    ex is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests }
        ? TimeSpan.FromSeconds(30)
        : TimeSpan.FromSeconds(attempt * 2);
```
