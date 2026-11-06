using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// A method whose arguments only a cold, early-returning arm takes the address of keeps them in
// the value frame on that arm alone: the frame copies each in where the arm begins, as the code
// before the branch left it, and the other arm reads the parameter. A reference argument the
// arm replaces lives only in the frame, which collections on the arm must keep alive.
public sealed class BranchValueFrameArgumentTests(CorrectnessTestRunner runner)
{
    [Theory]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    public void ArgumentsOnlyTheColdArmAddressesKeepTheirValuesAndRoots(string cell) =>
        runner.Run(new("BranchFrameArguments", "BranchFrameArguments", Source, [0, 3, 7])
        {
            Optimization = FinalWasmOptimization.O3,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });

    internal const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        namespace BranchFrameArguments;

        public static class EntryPoint
        {
            private static bool s_checking;
            private static string? s_last;
            private static long s_count;
            private static object? s_sink;

            public static int Trace() => 0;

            public static int Run(int input)
            {
                var hash = 17;
                for (var round = 0; round < 4; round++)
                {
                    s_checking = (round & 1) == 1;
                    Consume(input + 0.25 * round);
                    hash = Mix(hash, s_last?.Length ?? -1);
                    Consume(input * 1000L + round);
                    hash = Mix(hash, s_last?.Length ?? -1);
                    hash = Mix(hash, Inspect("text" + input, round == 2));
                    hash = Mix(hash, (int)Adjust(input + round, round == 3));
                }
                return Mix(hash, (int)s_count);
            }

            // As the micro-benchmark runner consumes results: the checking arm's constrained
            // ToString takes the argument's address. Only the text's length is compared, which
            // every culture's decimal separator keeps.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static void Consume<T>(T value)
            {
                s_count++;
                if (s_checking)
                {
                    Churn();
                    s_last = value switch
                    {
                        null => "null",
                        IFormattable => value.ToString(),
                        _ => "object",
                    };
                    return;
                }
                s_count += 2;
            }

            // The cold arm replaces the argument through its address with a string only the frame
            // holds, then collects.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static int Inspect(string text, bool cold)
            {
                if (cold)
                {
                    Replace(ref text);
                    Churn();
                    return text.Length * 3 + text[^1];
                }
                return text.Length + 1;
            }

            // The argument is written before the branch; the cold arm's frame copy starts from that.
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static long Adjust(long value, bool cold)
            {
                value += 3;
                if (cold)
                {
                    Mutate(ref value);
                    return value;
                }
                return value * 2;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Replace(ref string text) => text = new string('x', text.Length + 2) + "!";

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Mutate(ref long value) => value = value * 5 + 1;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Churn()
            {
                GC.Collect();
                for (var index = 0; index < 64; index++) s_sink = new string('y', index & 15);
                GC.Collect();
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);
        }
        """;
}
