using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ResilienceToolkit.Retry;

namespace RetryDemo;

internal class Program
{
    private static int _attemptCount;
    private const int FailuresBeforeSuccess = 3;
    private const int MaxAttempts = 5;

    private static async Task Main(string[] args)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        // This delegate is defined in RetryPolicy.cs and allows us to calculate the delay before each retry
        // based on the attempt number and the exception that was thrown.
        RetryDelayStrategy exponentialBackoff = static (attempt, _) =>
            TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt - 1), 8));

        // Testing a realistic scenario where we have a flaky network call that fails a few times before succeeding.
        //(remove comment) await UnstableNetworkTest(cts, exponentialBackoff);

        // Testing a simple scenario where we just want to retry a no-op operation a few times with a fixed delay.
        TimeSpan delay = TimeSpan.FromMilliseconds(500);
        await RetryPolicy.RetryAsync(LightWeightDemo, MaxAttempts, delay, cts.Token);

       
    }

    private static async Task UnstableNetworkTest(CancellationTokenSource cts, RetryDelayStrategy exponentialBackoff)
    {
        Console.WriteLine("Starting operation with exponential backoff...");
        try
        {
            string result = await RetryPolicy.RetryOnExceptionAsync<string, HttpRequestException>(
                operation: () => UnstableNetworkCallAsync(cts.Token),
                retries: MaxAttempts,
                delayStrategy: exponentialBackoff,
                cancellationToken: cts.Token,
                notifyException: (ex, attempt) =>
                {
                    Console.WriteLine($"Attempt {attempt} failed: {ex.Message}");

                    if (attempt < MaxAttempts)
                    {
                        TimeSpan nextDelay = exponentialBackoff(attempt, ex);
                        Console.WriteLine($"   -> waiting {nextDelay.TotalSeconds:N0} second(s) before retrying...");
                    }
                });

            Console.WriteLine($"\nSuccess: {result}");
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\n[SYSTEM] Operation was cancelled.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n[SYSTEM] Operation failed permanently: {ex.Message}");
        }
    }

    /// <summary>
    /// Simulates a flaky network call that fails a few times and then succeeds.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel the simulated network call.</param>
    /// <returns>A simulated response payload.</returns>
    /// <exception cref="HttpRequestException">
    /// Thrown for the first few attempts to simulate a transient HTTP failure.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled while the simulated work is running.
    /// </exception>
    private static async Task<string> UnstableNetworkCallAsync(CancellationToken cancellationToken)
    {
        _attemptCount++;
        Console.WriteLine($"   -> [Network Call] Starting attempt {_attemptCount}...");

        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);

        if (_attemptCount <= FailuresBeforeSuccess)
        {
            throw new HttpRequestException("Service Unavailable (503)");
        }

        return "Recovered response payload.";
    }

    private static async Task LightWeightDemo()
    {
        await Console.Out.WriteLineAsync("Doing nothing...");
    }
}