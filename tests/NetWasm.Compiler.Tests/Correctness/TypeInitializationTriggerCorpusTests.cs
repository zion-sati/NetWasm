using System.Linq;
using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// A type with an explicit static constructor is initialized when the program
// first creates one, calls one of its static methods or touches one of its
// static fields (ECMA-335 I.8.9.5), even when the result is unused; a base
// type's constructor initializes the base type. An optimization that drops the
// unused work keeps the initialization. Each case returns the counter its
// static constructors advance.
public sealed class TypeInitializationTriggerCorpusTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData(FinalWasmOptimization.Oz)]
    [InlineData(FinalWasmOptimization.O3)]
    public void UnusedWorkStillInitializesItsType(FinalWasmOptimization optimization) =>
        runner.Run(new(
            "TypeInitializationTriggerCorpus",
            "NetWasm.Correctness.TypeInitializationTriggers",
            Source,
            [.. Enumerable.Range(0, 9)])
        {
            Optimization = optimization,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });

    internal const string Source = """
            namespace NetWasm.Correctness.TypeInitializationTriggers;

            public static class Log { public static int Count; }

            public class Plugin { static Plugin() => Log.Count += 1; public Plugin() { } }
            public class InLoop { static InLoop() => Log.Count += 10; }
            public class WithArguments
            {
                public readonly int Left;
                public readonly int Right;
                static WithArguments() => Log.Count += 100;
                public WithArguments(int left, int right) { Left = left; Right = right; }
            }
            public class StrictBase { static StrictBase() => Log.Count += 1000; public StrictBase() { } }
            public class LazyDerived : StrictBase { public LazyDerived() { } }
            public class Generic<T> { static Generic() => Log.Count += 3; public Generic() { } }
            public class NoInitializer { public int Value = 7; }
            public class StaticField { public static int Value = 1; static StaticField() => Log.Count += 20; }
            public class StaticMethod { static StaticMethod() => Log.Count += 40; public static void Nothing() { } }
            public class StaticConstant { static StaticConstant() => Log.Count += 80; public static int Five => 5; }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    switch (input)
                    {
                        case 0: new Plugin(); break;
                        case 1: for (var i = 0; i < 3; i++) new InLoop(); break;
                        case 2: new WithArguments(1, 2); break;
                        case 3: new LazyDerived(); break;
                        case 4: new Generic<int>(); new Generic<string>(); new Generic<int>(); break;
                        case 5: new NoInitializer(); break;
                        case 6: _ = StaticField.Value; break;
                        case 7: StaticMethod.Nothing(); break;
                        case 8: _ = StaticConstant.Five; break;
                    }
                    return Log.Count;
                }

                public static int Trace() => 0;
            }
            """;
}
