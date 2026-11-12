namespace NetWasm.Compiler.Tests.Correctness;

// A value type's default Equals and GetHashCode run each reference field's own Equals and
// GetHashCode, which can allocate. The values compared or hashed are held only by that call, so
// it must publish them before it calls out: with a collection before every allocation, the
// second field was read from a freed box (a wrong result on wasm32, a trap on wasm64).
public sealed class ValueTypeEqualityRootsGcTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void DefaultEqualityAndHashKeepTheirValuesAcrossFieldCalls() =>
        runner.Run(new("ValueTypeEqualityRoots", "ValueTypeEqualityRoots", Source, [0, 1, 2, 3, 4, 5])
        {
            ReportAllMismatches = true,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            CollectsAtEveryAllocation = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        namespace ValueTypeEqualityRoots;

        public sealed class Overriding
        {
            public int Id;
            public override bool Equals(object? other) => other is Overriding value && ("x" + value.Id) == ("x" + Id);
            public override int GetHashCode() => ("h" + Id).Length;
        }

        public struct Loose { public object? Item; public string? Label; }
        public struct OnlyObject { public object? Item; }
        public struct OnlyString { public string? Label; }

        public static class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                var a = new Loose { Item = new Overriding { Id = 3 }, Label = "a" + input };
                var b = new Loose { Item = new Overriding { Id = 3 }, Label = "a" + input };
                var c = new OnlyObject { Item = new Overriding { Id = 3 } };
                var d = new OnlyObject { Item = new Overriding { Id = 3 } };
                var e = new OnlyString { Label = "s" + input };
                var f = new OnlyString { Label = "s" + input };
                return input switch
                {
                    0 => a.Equals(b) ? 1 : 0,
                    1 => a.GetHashCode() == b.GetHashCode() ? 1 : 0,
                    2 => c.Equals(d) ? 1 : 0,
                    3 => c.GetHashCode() == d.GetHashCode() ? 1 : 0,
                    4 => e.Equals(f) ? 1 : 0,
                    _ => e.GetHashCode() == f.GetHashCode() ? 1 : 0,
                };
            }
        }
        """;
}
