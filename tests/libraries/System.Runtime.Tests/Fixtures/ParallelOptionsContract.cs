// From the compiler harness's ParallelOptionsContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Threading;
using System.Threading.Tasks;
namespace ParallelOptionsFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var options = new ParallelOptions();
        if (options.MaxDegreeOfParallelism != -1 || options.CancellationToken.CanBeCanceled || !ReferenceEquals(options.TaskScheduler, TaskScheduler.Default)) return 1;
        options.MaxDegreeOfParallelism = 1;
        try { options.MaxDegreeOfParallelism = 0; return 2; } catch (ArgumentOutOfRangeException e) { if (e.ParamName != "MaxDegreeOfParallelism") return 3; }
        try { options.MaxDegreeOfParallelism = -2; return 4; } catch (ArgumentOutOfRangeException) { }
        if (options.MaxDegreeOfParallelism != 1) return 5;
        options.MaxDegreeOfParallelism = int.MaxValue;
        if (options.MaxDegreeOfParallelism != int.MaxValue) return 6;
        options.MaxDegreeOfParallelism = -1;
        options.TaskScheduler = null;
        if (options.TaskScheduler != null) return 7;
        options.TaskScheduler = TaskScheduler.Current;
        if (!ReferenceEquals(options.TaskScheduler, TaskScheduler.Current)) return 8;
        using var source = new CancellationTokenSource();
        options.CancellationToken = source.Token;
        if (options.CancellationToken != source.Token || options.CancellationToken.IsCancellationRequested) return 9;
        source.Cancel();
        if (!options.CancellationToken.IsCancellationRequested) return 10;
        var result = default(ParallelLoopResult);
        if (result.IsCompleted || result.LowestBreakIteration.HasValue) return 11;
        return input + 1;
    }
}
