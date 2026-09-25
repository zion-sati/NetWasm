namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class ControlFlowDifferentialTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DebugAndReleaseControlFlowMatchesDesktopDotNet()
    {
        runner.Run(new(
            "ControlFlowDifferential",
            "NetWasm.Correctness.ControlFlow",
            """
            namespace NetWasm.Correctness.ControlFlow;

            public enum Mode
            {
                Zero,
                One,
                Many,
            }

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = 1;
                    var total = input > 0 && input < 100 ? 3 : 5;
                    total += input == 0 || input == 41 ? 7 : 11;

                    var index = 0;
                    while (index < input % 3)
                    {
                        total += index++;
                    }
                    do
                    {
                        total++;
                    }
                    while (total < 0);
                    for (var outer = 0; outer < 3; outer++)
                    {
                        for (var inner = 0; inner < 3; inner++)
                        {
                            if (inner == 0) continue;
                            if (outer == 2) break;
                            total += outer + inner;
                        }
                    }
                    foreach (var value in new[] { 1, 2, 3 })
                    {
                        total += value;
                    }

                    var mode = input switch
                    {
                        0 => Mode.Zero,
                        1 => Mode.One,
                        _ => Mode.Many,
                    };
                    total += mode switch
                    {
                        Mode.Zero => 13,
                        Mode.One => 17,
                        _ => 19,
                    };
                    total += input switch
                    {
                        -100 => 23,
                        4 => 29,
                        41 => 31,
                        _ => 37,
                    };
                    var text = input == 41 ? "answer" : input == 0 ? "zero" : "other";
                    total += text switch
                    {
                        "answer" => 41,
                        "zero" => 43,
                        _ => 47,
                    };

                    var turns = 0;
                Backward:
                    turns++;
                    if (turns < 2) goto Backward;
                    if (input < 0) goto Negative;
                    total += 53;
                    goto Done;
                Negative:
                    total += 59;
                Done:
                    _trace = total;
                    return total;
                }

                public static int Trace() => _trace;
            }
            """,
            [-100, -1, 0, 1, 4, 41, 100]));
    }
}
