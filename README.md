# ResilienceToolkit.Retry

A lightweight, zero-dependency retry utility for async operations in .NET.

## Features

- Configurable **retry count**
- Pluggable **delay strategies** (constant, exponential, or any custom delegate)
- **Exception filtering** — retry only on a specific exception type
- **Cancellation support** via `CancellationToken`

## Usage

```csharp
using ResilienceToolkit.Retry;

RetryDelayStrategy exponentialBackoff = static (attempt, _) =>
    TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 8));

string result = await RetryPolicy.RetryOnExceptionAsync<string, HttpRequestException>(
    operation: () => CallExternalServiceAsync(cancellationToken),
    retries: 5,
    delayStrategy: exponentialBackoff,
    cancellationToken: cancellationToken,
    notifyException: (ex, attempt) =>
        Console.WriteLine($"Attempt {attempt} failed: {ex.Message}"));
```

A runnable version of this example lives in [`samples/RetryDemo`](samples/RetryDemo).

## Delay Strategies

A `RetryDelayStrategy` is a delegate with the signature `(int attemptNumber, Exception exception) => TimeSpan`.
The caller decides the strategy, keeping the library simple and composable.

| Strategy    | Example                                                                          |
|-------------|----------------------------------------------------------------------------------|
| Constant    | `(_, _) => TimeSpan.FromSeconds(2)`                                              |
| Exponential | `(attempt, _) => TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 8))`   |

See [`docs/delay-strategies.md`](docs/delay-strategies.md) for more detail and examples.

## API Overview

| Method                                        | Description                                              |
|-----------------------------------------------|----------------------------------------------------------|
| `RetryAsync<TResult>`                         | Retry a value-returning operation on any exception       |
| `RetryAsync`                                  | Retry a void operation on any exception                  |
| `RetryOnExceptionAsync<TResult, TException>`  | Retry a value-returning operation on a specific exception|
| `RetryOnExceptionAsync<TException>`           | Retry a void operation on a specific exception           |

Each method has two overloads: one accepting a fixed `TimeSpan` and one accepting a `RetryDelayStrategy`.

See [`docs/retry-policy.md`](docs/retry-policy.md) for full API documentation.

## License

License: CC BY-NC 4.0 — free for personal and non-commercial use only.

---

*Created by Vladimir Bosnjak, Nijmegen, March 28, 2026*
