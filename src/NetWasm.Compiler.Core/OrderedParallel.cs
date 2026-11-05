using System;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace NetWasm.Compiler.Core;

/// <summary>
/// Runs independent per-item work (validation, metrics) on all cores while behaving like a
/// loop over the items in order: results keep their positions, and when items fail, the
/// failure of the first failing item is the one thrown.
/// </summary>
public static class OrderedParallel
{
    // Below this many items the work runs in turn.
    private const int SerialThreshold = 64;

    public static void For(int count, Action<int> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        _ = Map(count, index =>
        {
            body(index);
            return true;
        });
    }

    public static TResult[] Map<TResult>(int count, Func<int, TResult> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var results = new TResult[count];
        if (count < SerialThreshold)
        {
            for (var index = 0; index < count; index++) results[index] = body(index);
            return results;
        }
        var failures = new ExceptionDispatchInfo?[count];
        Parallel.For(0, count, index =>
        {
            try
            {
                results[index] = body(index);
            }
#pragma warning disable CA1031 // Rethrown below, in item order.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                failures[index] = ExceptionDispatchInfo.Capture(exception);
            }
        });
        foreach (var failure in failures) failure?.Throw();
        return results;
    }
}
