using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class TypedTraceDifferentialTests
{
    [Fact]
    public void ComparesOrderedTypedTraceRecordsAcrossDesktopAndNetWasm()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var runner = services.GetRequiredService<IDifferentialCorpusRunner>();
        runner.Run(new(
            "TypedTraceDifferential",
            "NetWasm.Correctness.TypedTrace",
            """
            namespace NetWasm.Correctness.TypedTrace;

            public static class EntryPoint
            {
                private static readonly int[] Kinds = new int[4];
                private static readonly int[] Ids = new int[4];
                private static readonly int[] Lows = new int[4];
                private static readonly int[] Highs = new int[4];
                private static int _count;

                public static int Run(int input)
                {
                    _count = 0;
                    Add(0, 1, 0, 0);
                    Add(1, 2, input == 0 ? 0 : 1, 0);
                    Add(2, 3, input, input < 0 ? -1 : 0);
                    return input + 1;
                }

                private static void Add(int kind, int id, int low, int high)
                {
                    var index = _count++;
                    Kinds[index] = kind;
                    Ids[index] = id;
                    Lows[index] = low;
                    Highs[index] = high;
                }

                public static int Trace() => _count;
                public static int TraceCount() => _count;
                public static int TraceKind(int index) => Kinds[index];
                public static int TraceEventId(int index) => Ids[index];
                public static int TracePayloadLow(int index) => Lows[index];
                public static int TracePayloadHigh(int index) => Highs[index];
            }
            """,
            [0, 7, -1])
        {
            UsesTypedTrace = true,
        });
    }
}
