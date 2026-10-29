using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ClubTimerXbox.Services;

// Changes include replacements and deletions, not only collection counts.
internal static class FinancialInputRevision
{
    private static long _value;
    internal static long Value => Interlocked.Read(ref _value);
    internal static void Changed() => Interlocked.Increment(ref _value);
}

internal sealed class BackgroundFinancialCalculation
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    internal sealed record Profile(double CaptureMilliseconds, double CalculationMilliseconds,
        double ApplyMilliseconds, int Attempts, int OwnerThreadId, int WorkerThreadId, bool SynchronousFallback);

    internal async Task<T> RunAsync<T>(Func<Func<CancellationToken, T>> capture,
        Func<object> version, Func<bool> scopeValid, Action<T> apply, CancellationToken token,
        Action<Profile>? measured = null)
    {
        await _gate.WaitAsync(token);
        try
        {
            double captureMilliseconds = 0, calculationMilliseconds = 0;
            int ownerThread = Environment.CurrentManagedThreadId, workerThread = 0;
            for (int attempt = 0; ; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (!scopeValid()) throw new OperationCanceledException("Snapshot scope changed.", token);
                var watch = Stopwatch.StartNew();
                var calculate = capture();
                captureMilliseconds += watch.Elapsed.TotalMilliseconds;
                object capturedVersion = version();
                watch.Restart();
                // A bounded synchronous fallback prevents continuous activity from starving sync.
                T result = attempt < 2
                    ? await Task.Run(() => { workerThread = Environment.CurrentManagedThreadId; return calculate(token); }, token)
                    : calculate(token);
                calculationMilliseconds += watch.Elapsed.TotalMilliseconds;
                token.ThrowIfCancellationRequested();
                if (!scopeValid()) throw new OperationCanceledException("Snapshot scope changed.", token);
                if (!Equals(capturedVersion, version()))
                {
                    if (attempt >= 2) throw new InvalidOperationException("Snapshot inputs keep changing outside their owner.");
                    continue;
                }
                watch.Restart();
                apply(result);
                measured?.Invoke(new(captureMilliseconds, calculationMilliseconds,
                    watch.Elapsed.TotalMilliseconds, attempt + 1, ownerThread, workerThread, attempt >= 2));
                return result;
            }
        }
        finally { _gate.Release(); }
    }
}
