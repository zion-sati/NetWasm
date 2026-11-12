namespace NetWasm.Compiler.Tests.Correctness;

// A function pointer can return a value type (issue 226's sibling: calli with a value-type return
// failed with NW1001): small and large structs, structs holding references, results passed on to
// another pointer, and pointers chosen at run time. Each program runs as desktop .NET does; the
// reference-holding results also keep their references across collections.
public sealed class FunctionPointerValueTypeCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void FunctionPointersReturnValueTypesAsDesktopDoes() =>
        runner.Run(new("FunctionPointerValueTypes", "FunctionPointerValueTypes", Source, [0, 3, 7])
        {
            AllowUnsafe = true,
            ReportAllMismatches = true,
        });

    [Fact]
    public void ValueTypeResultsKeepTheirReferencesAcrossCollections() =>
        runner.Run(new("FunctionPointerValueTypeRoots", "FunctionPointerValueTypes", Source, [7])
        {
            AllowUnsafe = true,
            ExpectedReturnValue = Expected,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            CollectsAtEveryAllocation = true,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });

    // Run(7) on desktop .NET.
    private const int Expected = 24409;

    private const string Source = """
        using System;
        namespace FunctionPointerValueTypes;

        public struct Pair { public int Left; public int Right; }
        public struct Wide { public long A, B, C, D, E; }
        public struct Named { public string Text; public int Length; public object? Tag; }

        public static unsafe class EntryPoint
        {
            public static int Trace() => 0;

            private static Pair MakePair(int value) => new() { Left = value, Right = value * 2 };
            private static Pair Swap(Pair pair) => new() { Left = pair.Right, Right = pair.Left };
            private static Pair Add(Pair left, Pair right) => new() { Left = left.Left + right.Left, Right = left.Right + right.Right };
            private static Wide MakeWide(int seed) => new() { A = seed, B = seed + 1L, C = (long)seed << 33, D = -seed, E = seed * 7L };
            private static Named MakeNamed(int value) => new() { Text = "n" + value, Length = value, Tag = new int[value] };
            private static Named Rename(Named named) => new() { Text = named.Text + "!", Length = named.Length + 1, Tag = named.Tag };

            public static int Run(int input)
            {
                delegate*<int, Pair> make = &MakePair;
                delegate*<Pair, Pair> swap = &Swap;
                delegate*<Pair, Pair, Pair> add = input % 2 == 0 ? &Add : &Add;
                var pair = add(swap(make(input)), make(input + 1));
                delegate*<int, Wide> wide = &MakeWide;
                var w = wide(input);
                delegate*<int, Named> named = &MakeNamed;
                delegate*<Named, Named> rename = &Rename;
                var renamed = rename(named(input));
                var garbage = new object[16];
                for (var i = 0; i < garbage.Length; i++) garbage[i] = new string('x', i);
                return pair.Left * 1000 + pair.Right * 100 + (int)(w.A + w.B + (w.C >> 33) + w.D + w.E) +
                    renamed.Text.Length * 10 + renamed.Length + ((int[])renamed.Tag!).Length;
            }
        }
        """;
}
