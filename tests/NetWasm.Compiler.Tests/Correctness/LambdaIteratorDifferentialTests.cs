namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class LambdaIteratorDifferentialTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DebugAndReleaseClosuresAndIteratorsMatchDesktopDotNet()
    {
        runner.Run(new(
            "LambdaIteratorDifferential",
            "NetWasm.Correctness.StateMachines",
            """
            using System;
            using System.Collections.Generic;

            namespace NetWasm.Correctness.StateMachines;

            public sealed class Capture
            {
                public int Value;

                public Capture(int value) => Value = value;
            }

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 1;
                    var offset = input + 1;
                    var capture = new Capture(2);
                    Func<int, int> projection = value => value + offset + capture.Value;
                    var total = Local(input);
                    foreach (var value in Values(input))
                    {
                        total += projection(value);
                        if (value > input)
                        {
                            break;
                        }
                    }
                    _trace += total;
                    return total;

                    int Local(int value) => value * 2;
                }

                public static int Trace() => _trace;

                private static IEnumerable<int> Values(int input)
                {
                    try
                    {
                        for (var outer = 0; outer < 2; outer++)
                        {
                            for (var inner = 0; inner < 2; inner++)
                            {
                                yield return input + outer + inner;
                                if (input < 0) yield break;
                            }
                        }
                    }
                    finally
                    {
                        _trace += 1000;
                    }
                }
            }
            """,
            [-1, 0, 1, 41]));
    }
}
