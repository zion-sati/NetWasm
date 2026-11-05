// Times every generated benchmark case whose id contains the filter, or, for a
// filter starting with '!', none of its '|'-separated parts: a warm-up
// call, then a batch doubled until it lasts 20 ms, then the fastest of five
// batches as nanoseconds per call. Prints "id<TAB>ns" or "id<TAB>ERROR message".
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

internal sealed record BenchmarkCase(string Id, Func<Action> Prepare);

internal static class Runner
{
    private static object? s_sink;
    private static long s_count;
    private static bool s_checking;
    private static string? s_last;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Consume<T>(T value)
    {
        s_count++;
        if (s_checking)
        {
            // Only values whose text needs no runtime type name: NetWasm's
            // Object.ToString throws without compiler-generated name facts.
            s_last = value switch
            {
                null => "null",
                string or bool or char or IFormattable => value.ToString(),
                Array array => $"array[{array.Length}]",
                _ => "object",
            };
            return;
        }
        if (typeof(T).IsValueType) return;
        s_sink = value;
    }

    // Span results, which a ref struct cannot pass as T: the length is the observable result.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Consume<T>(Span<T> value) => Consume<int>(value.Length);

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Consume<T>(ReadOnlySpan<T> value) => Consume<int>(value.Length);

    public static int Run(string[] args)
    {
        var filter = args.Length > 0 ? args[0] : "";
        var excluding = filter.StartsWith('!');
        var parts = (excluding ? filter[1..] : filter).Split('|');
        var list = args.Length > 1 && args[1] == "list";
        // check: run each case three times and print its last result, to compare runtimes.
        s_checking = args.Length > 1 && args[1] == "check";
        var batchTicks = Stopwatch.Frequency / 50;
        foreach (var benchmark in BenchmarkTable.All())
        {
            var matches = false;
            foreach (var part in parts) matches |= benchmark.Id.Contains(part, StringComparison.Ordinal);
            if (matches == excluding) continue;
            if (list)
            {
                Console.WriteLine(benchmark.Id);
                continue;
            }
            try
            {
                var action = benchmark.Prepare();
                action();
                if (s_checking)
                {
                    s_last = null;
                    action();
                    action();
                    var text = s_last ?? "void";
                    Console.WriteLine($"{benchmark.Id}\tok {(text.Length > 80 ? text[..80] : text)}");
                    continue;
                }
                long batch = 1;
                long elapsed;
                while (true)
                {
                    elapsed = Time(action, batch);
                    if (elapsed >= batchTicks || batch >= 1L << 30) break;
                    batch *= 2;
                }
                var best = elapsed;
                for (var sample = 0; sample < 4; sample++) best = Math.Min(best, Time(action, batch));
                var nanoseconds = best * 1_000_000_000.0 / Stopwatch.Frequency / batch;
                Console.WriteLine($"{benchmark.Id}\t{nanoseconds:F2}");
            }
            catch (Exception exception)
            {
                // The message, not the type name: NetWasm has no runtime type names.
                var message = exception.Message.Replace('\t', ' ').Replace('\n', ' ');
                Console.WriteLine($"{benchmark.Id}\tERROR {(message.Length > 120 ? message[..120] : message)}");
            }
        }
        GC.KeepAlive(s_sink);
        return s_count < 0 ? 1 : 0;
    }

    private static long Time(Action action, long count)
    {
        var start = Stopwatch.GetTimestamp();
        for (long index = 0; index < count; index++) action();
        return Stopwatch.GetTimestamp() - start;
    }
}
