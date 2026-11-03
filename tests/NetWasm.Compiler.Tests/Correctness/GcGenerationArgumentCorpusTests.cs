using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// GC.Collect and GC.CollectionCount accept any non-negative generation, as desktop .NET does;
// a negative one throws ArgumentOutOfRangeException. NetWasm collects every generation at
// once, so the cases check only what holds on both: counts never fall as the generation rises.
public sealed class GcGenerationArgumentCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void GenerationArgumentsMatchDesktop(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "GcGenerationArgumentCorpus",
            "NetWasm.Correctness.GcGenerationArguments",
            Source,
            [.. Enumerable.Range(0, 8)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            using System;

            namespace NetWasm.Correctness.GcGenerationArguments;

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Code(() => { GC.Collect(2); return 1; }),
                    1 => Code(() => { GC.Collect(3, GCCollectionMode.Forced); return 1; }),
                    2 => Code(() => GC.CollectionCount(2) >= 0 ? 1 : 0),
                    3 => Code(() => { GC.Collect(-1); return 1; }),
                    4 => Code(() => GC.CollectionCount(-1)),
                    5 => Code(() => { GC.Collect(); return GC.CollectionCount(2) <= GC.CollectionCount(0) ? 1 : 0; }),
                    6 => Code(() => GC.CollectionCount(int.MaxValue) >= 0 ? 1 : 0),
                    7 => Code(() => GC.GetGeneration(new object()) <= GC.MaxGeneration ? 1 : 0),
                    _ => -1,
                };

                public static int Trace() => 0;

                private static int Code(Func<int> action)
                {
                    try
                    {
                        return action();
                    }
                    catch (ArgumentOutOfRangeException)
                    {
                        return -100;
                    }
                }
            }
            """;
}
