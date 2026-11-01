namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ArrayCloneCompatibilityTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ArrayCloneMatchesDesktopAcrossProfilesAndTargets()
    {
        runner.Run(new CorpusFixture(
            "ArrayCloneCompatibility",
            "NetWasm.Correctness.ArrayClone",
            Source,
            [0, 1, 2, 3])
        {
            CaptureCompilerDiagnostics = true,
            EmitStackTrace = true,
            RequiresReactor = true,
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "Array.Clone is implemented by the NetWasm runtime for every supported array representation.",
        }.WithFullExecutionMatrix());
    }

    private const string Source = """
        using System;

        namespace NetWasm.Correctness.ArrayClone;

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => CloneValueArray(),
                1 => CloneReferenceArray(),
                2 => CloneValueArrayWithReferences(),
                _ => CloneRectangularArray(),
            };

            public static int Trace() => 0;

            private static int CloneValueArray()
            {
                int[] source = [3, 5, 8];
                var clone = (int[])source.Clone();
                clone[1] = 13;
                return Bool(!ReferenceEquals(source, clone) && source[1] == 5 &&
                    clone.Length == 3 && clone[0] == 3 && clone[1] == 13 && clone[2] == 8, 300);
            }

            private static int CloneReferenceArray()
            {
                var item = new Item(7);
                Item[] source = [item, new Item(11)];
                var clone = (Item[])source.Clone();
                clone[1] = item;
                return Bool(!ReferenceEquals(source, clone) && ReferenceEquals(source[0], clone[0]) &&
                    source[1].Value == 11 && ReferenceEquals(clone[0], clone[1]), 301);
            }

            private static int CloneValueArrayWithReferences()
            {
                var item = new Item(17);
                Pair[] source = [new Pair(item, 19)];
                var clone = (Pair[])source.Clone();
                clone[0].Number = 23;
                return Bool(!ReferenceEquals(source, clone) && ReferenceEquals(source[0].Item, clone[0].Item) &&
                    source[0].Number == 19 && clone[0].Number == 23, 302);
            }

            private static int CloneRectangularArray()
            {
                var item = new Item(29);
                var source = new Item[2, 2];
                source[0, 1] = item;
                source[1, 0] = new Item(31);
                var clone = (Item[,])source.Clone();
                clone[1, 0] = item;
                return Bool(!ReferenceEquals(source, clone) && clone.Rank == 2 &&
                    clone.GetLength(0) == 2 && clone.GetLength(1) == 2 &&
                    ReferenceEquals(source[0, 1], clone[0, 1]) &&
                    source[1, 0].Value == 31 && ReferenceEquals(clone[0, 1], clone[1, 0]), 303);
            }

            private static int Bool(bool condition, int value) => condition ? value : -value;

            private sealed class Item(int value)
            {
                public int Value { get; } = value;
            }

            private struct Pair(Item item, int number)
            {
                public Item Item = item;
                public int Number = number;
            }
        }
        """;
}
