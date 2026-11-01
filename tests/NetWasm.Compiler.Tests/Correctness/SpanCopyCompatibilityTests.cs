namespace NetWasm.Compiler.Tests.Correctness;

public sealed class SpanCopyCompatibilityTests(CorrectnessTestRunner runner)
    : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void SpanCopiesMatchDesktopWithoutTemporaryArrays() =>
        Run(new(
            "SpanCopyCompatibility",
            "NetWasm.Correctness.SpanCopyCompatibility",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
        {
            AllowUnsafe = true,
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(
                CorpusMatrixProfile.Extended,
                Backend: CorpusExecutionBackend.Linked),
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "The fixture compares overlap-safe Span copying against the production runtime.",
        });

    private const string Source = """
        using System;

        namespace NetWasm.Correctness.SpanCopyCompatibility;

        public sealed class Box
        {
            public Box(int value) => Value = value;

            public int Value { get; }
        }

        public struct Pair
        {
            public Box? First;
            public Box? Second;
        }

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => CopyValuesForward(),
                1 => CopyValuesBackward(),
                2 => CopyReferencesForward(),
                3 => CopyReferencesBackward(),
                4 => CopyReferenceStructsAndCollect(),
                5 => ReadOnlyCopyAndTryCopy(),
                6 => FailedCopiesDoNotMutate(),
                7 => EmptyAndSameRegionCopies(),
                8 => ToArrayCopiesExactlyOnce(),
                9 => PointerMemoryCopyOverlaps(),
                _ => PointerMemoryCopySignedValidation(),
            };

            public static int Trace() => 0;

            private static int CopyValuesForward()
            {
                var values = new[] { 1, 2, 3, 4, 5 };
                values.AsSpan(1, 4).CopyTo(values.AsSpan(0, 4));
                return Matches(values, 2, 3, 4, 5, 5) ? 42 : -1;
            }

            private static int CopyValuesBackward()
            {
                var values = new[] { 1, 2, 3, 4, 5 };
                values.AsSpan(0, 4).CopyTo(values.AsSpan(1, 4));
                return Matches(values, 1, 1, 2, 3, 4) ? 42 : -2;
            }

            private static int CopyReferencesForward()
            {
                var one = new Box(1);
                var two = new Box(2);
                var three = new Box(3);
                var four = new Box(4);
                var values = new[] { one, two, three, four };
                values.AsSpan(1, 3).CopyTo(values.AsSpan(0, 3));
                GC.Collect();
                return ReferenceEquals(values[0], two) &&
                    ReferenceEquals(values[1], three) &&
                    ReferenceEquals(values[2], four) &&
                    ReferenceEquals(values[3], four)
                    ? 42
                    : -3;
            }

            private static int CopyReferencesBackward()
            {
                var one = new Box(1);
                var two = new Box(2);
                var three = new Box(3);
                var four = new Box(4);
                var values = new[] { one, two, three, four };
                values.AsSpan(0, 3).CopyTo(values.AsSpan(1, 3));
                GC.Collect();
                return ReferenceEquals(values[0], one) &&
                    ReferenceEquals(values[1], one) &&
                    ReferenceEquals(values[2], two) &&
                    ReferenceEquals(values[3], three)
                    ? 42
                    : -4;
            }

            private static int CopyReferenceStructsAndCollect()
            {
                var one = new Box(1);
                var two = new Box(2);
                var three = new Box(3);
                var values = new[]
                {
                    new Pair { First = one, Second = two },
                    new Pair { First = two, Second = three },
                    new Pair { First = three, Second = one },
                };
                values.AsSpan(0, 2).CopyTo(values.AsSpan(1, 2));
                GC.Collect();
                return ReferenceEquals(values[1].First, one) &&
                    ReferenceEquals(values[1].Second, two) &&
                    ReferenceEquals(values[2].First, two) &&
                    ReferenceEquals(values[2].Second, three)
                    ? 42
                    : -5;
            }

            private static int ReadOnlyCopyAndTryCopy()
            {
                var values = new[] { 1, 2, 3, 4 };
                ReadOnlySpan<int> source = values.AsSpan(0, 3);
                source.CopyTo(values.AsSpan(1, 3));
                if (!Matches(values, 1, 1, 2, 3))
                {
                    return -6;
                }

                ReadOnlySpan<int> second = values.AsSpan(1, 3);
                return second.TryCopyTo(values.AsSpan(0, 3)) &&
                    Matches(values, 1, 2, 3, 3)
                    ? 42
                    : -7;
            }

            private static int FailedCopiesDoNotMutate()
            {
                var source = new[] { 1, 2 };
                var destination = new[] { 9 };
                var threw = false;
                try
                {
                    source.AsSpan().CopyTo(destination);
                }
                catch (ArgumentException)
                {
                    threw = true;
                }

                var tryResult = ((ReadOnlySpan<int>)source).TryCopyTo(destination);
                return threw && !tryResult && destination[0] == 9 ? 42 : -8;
            }

            private static int EmptyAndSameRegionCopies()
            {
                var values = new[] { 1, 2, 3 };
                values.AsSpan().CopyTo(values.AsSpan());
                Span<int>.Empty.CopyTo(Span<int>.Empty);
                return Matches(values, 1, 2, 3) ? 42 : -9;
            }

            private static int ToArrayCopiesExactlyOnce()
            {
                var one = new Box(1);
                var two = new Box(2);
                ReadOnlySpan<Box> source = new[] { one, two };
                var copy = source.ToArray();
                GC.Collect();
                return copy.Length == 2 &&
                    ReferenceEquals(copy[0], one) &&
                    ReferenceEquals(copy[1], two)
                    ? 42
                    : -10;
            }

            private static unsafe int PointerMemoryCopyOverlaps()
            {
                var values = new byte[] { 1, 2, 3, 4, 5 };
                fixed (byte* pointer = values)
                {
                    Buffer.MemoryCopy(pointer, pointer + 1, 4, 4);
                    if (values[0] != 1 || values[1] != 1 || values[2] != 2 ||
                        values[3] != 3 || values[4] != 4)
                    {
                        return -11;
                    }

                    Buffer.MemoryCopy(pointer + 1, pointer, 4UL, 4UL);
                }

                return values[0] == 1 && values[1] == 2 && values[2] == 3 &&
                    values[3] == 4 && values[4] == 4
                    ? 42
                    : -12;
            }

            private static unsafe int PointerMemoryCopySignedValidation()
            {
                var sourceNegativeOverflowed = false;
                try
                {
                    Buffer.MemoryCopy(null, null, 0, -1);
                }
                catch (OverflowException)
                {
                    sourceNegativeOverflowed = true;
                }

                var destinationTooSmall = false;
                try
                {
                    Buffer.MemoryCopy(null, null, -1, 0);
                }
                catch (ArgumentOutOfRangeException)
                {
                    destinationTooSmall = true;
                }

                return sourceNegativeOverflowed && destinationTooSmall
                    ? 42
                    : -13;
            }

            private static bool Matches(int[] values, params int[] expected)
            {
                if (values.Length != expected.Length)
                {
                    return false;
                }
                for (var index = 0; index < values.Length; index++)
                {
                    if (values[index] != expected[index])
                    {
                        return false;
                    }
                }
                return true;
            }
        }
        """;
}
