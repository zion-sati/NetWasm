namespace NetWasm.Compiler.Tests.Correctness;

// A Debug build branches to the next instruction for an if whose body calls a
// partial method without an implementation or a [Conditional] method whose
// symbol is undefined: the call is removed, the branch stays (issue 221). Each
// shape puts such a branch where the structurer meets it differently, and the
// program's results must match desktop .NET.
public sealed class ConditionalBranchToFallthroughCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void BranchesToTheNextInstructionCompileAndRunInEveryControlFlowShape() =>
        runner.Run(new("ConditionalBranchToFallthrough", "ConditionalBranchToFallthrough", Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9])
        {
            Matrix = new(CorpusMatrixProfile.Extended),
        });

    private const string Source = """
        using System;
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.Threading.Tasks;
        namespace ConditionalBranchToFallthrough;

        public static partial class Shapes
        {
            static partial void OnChanged(int value);

            [Conditional("NETWASM_NEVER_DEFINED")]
            private static void Log(int value) => throw new InvalidOperationException(value.ToString());

            // The issue's program.
            public static int Set(bool changed)
            {
                if (changed)
                    OnChanged(1);
                return changed ? 1 : 2;
            }

            public static int Compared(int a, int b)
            {
                if (a < b) Log(a);
                if (a == b) OnChanged(b);
                if (a >= b) Log(b);
                return a - b;
            }

            public static int InLoop(int count)
            {
                var sum = 0;
                for (var index = 0; index < count; index++)
                {
                    if (index % 2 == 0) Log(index);
                    sum += index;
                    if (sum > 100) OnChanged(sum);
                }
                return sum;
            }

            public static int InTry(int value)
            {
                var result = 0;
                try
                {
                    if (value > 3) OnChanged(value);
                    result = value * 2;
                }
                finally
                {
                    if (result > 0) Log(result);
                    result++;
                }
                return result;
            }

            public static int InHandler(int value)
            {
                try
                {
                    if (value > 0) throw new InvalidOperationException();
                }
                catch (InvalidOperationException) when (value > 1)
                {
                    if (value > 2) Log(value);
                    return value;
                }
                catch (InvalidOperationException)
                {
                    if (value < 5) OnChanged(value);
                    return -value;
                }
                return 0;
            }

            public static IEnumerable<int> Iterate(int count)
            {
                for (var index = 0; index < count; index++)
                {
                    if (index > 1) Log(index);
                    yield return index;
                }
            }

            public static async Task<int> Await(int value)
            {
                if (value > 0) OnChanged(value);
                var completed = await Task.FromResult(value);
                if (completed > 1) Log(completed);
                return completed + 1;
            }

            public static int InSwitch(int value)
            {
                switch (value % 3)
                {
                    case 0:
                        if (value > 3) Log(value);
                        return 1;
                    case 1:
                        return 2;
                    default:
                        if (value > 4) OnChanged(value);
                        return 3;
                }
            }

            public static int InLambda(int value)
            {
                Func<int, int> triple = argument =>
                {
                    if (argument > 2) Log(argument);
                    return argument * 3;
                };
                return triple(value);
            }

            public static int OnlyBranch(int value)
            {
                if (value != 0) OnChanged(value);
                return 7;
            }
        }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                var sum = 0;
                foreach (var item in Shapes.Iterate(input)) sum += item;
                return input switch
                {
                    0 => Shapes.Set(true) * 10 + Shapes.Set(false),
                    1 => Shapes.Compared(input, 4) + Shapes.Compared(4, input) + Shapes.Compared(4, 4),
                    2 => Shapes.InLoop(input * 9),
                    3 => Shapes.InTry(input) + Shapes.InTry(input + 2),
                    4 => Shapes.InHandler(0) + Shapes.InHandler(1) * 10 + Shapes.InHandler(input) * 100,
                    5 => sum,
                    6 => Shapes.Await(input).GetAwaiter().GetResult() + Shapes.Await(0).GetAwaiter().GetResult(),
                    7 => Shapes.InSwitch(input) + Shapes.InSwitch(input + 1) * 10 + Shapes.InSwitch(input + 2) * 100,
                    8 => Shapes.InLambda(input),
                    _ => Shapes.OnlyBranch(input) + Shapes.OnlyBranch(0),
                };
            }
        }
        """;
}
