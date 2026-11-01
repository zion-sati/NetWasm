namespace NetWasm.Compiler.Tests.Correctness;

public sealed class NonGenericArrayCompatibilityTests(CorrectnessTestRunner runner)
    : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void NonGenericVectorOperationsMatchDesktopOnRealRuntimes() =>
        Run(new(
            "NonGenericArrayCompatibility",
            "NetWasm.Correctness.NonGenericArrayCompatibility",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(
                CorpusMatrixProfile.Extended,
                Backend: CorpusExecutionBackend.Linked),
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "The fixture compares non-generic vector algorithms and reliable copying against the production runtime.",
        });

    [Fact]
    public void EnumVectorSortRootsItsOwnBoxedElements() =>
        Run(new(
            "NonGenericEnumArraySortReachability",
            "NetWasm.Correctness.NonGenericEnumArraySortReachability",
            EnumSortSource,
            [0])
        {
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(
                CorpusMatrixProfile.Extended,
                Backend: CorpusExecutionBackend.Linked),
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "The fixture proves that non-generic enum sorting retains its own boxed element representation.",
        });

    private const string EnumSortSource = """
        using System;

        namespace NetWasm.Correctness.NonGenericEnumArraySortReachability;

        public enum Marker
        {
            One = 1,
            Two = 2,
            Three = 3,
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Array markers = new[] { Marker.Three, Marker.One, Marker.Two };
                Array.Sort(markers);
                var values = (Marker[])markers;
                return values[0] == Marker.One &&
                    values[1] == Marker.Two &&
                    values[2] == Marker.Three
                    ? 42
                    : -1;
            }

            public static int Trace() => 0;
        }
        """;

    private const string Source = """
        using System;
        using System.Collections;

        namespace NetWasm.Correctness.NonGenericArrayCompatibility;

        public readonly struct Point : IComparable<Point>, IComparable
        {
            public Point(int value) => Value = value;

            public int Value { get; }

            public int CompareTo(Point other) => Value.CompareTo(other.Value);

            public int CompareTo(object? other) => CompareTo((Point)other!);

            public override bool Equals(object? value) =>
                value is Point point && point.Value == Value;

            public override int GetHashCode() => Value;
        }

        public enum Marker
        {
            One = 1,
            Two = 2,
            Three = 3,
        }

        public sealed class DescendingComparer : IComparer
        {
            public int Compare(object? left, object? right) =>
                ((int)right!).CompareTo((int)left!);
        }

        public sealed class ThrowingComparer : IComparer
        {
            public int Compare(object? left, object? right) =>
                throw new ArgumentException();
        }

        public sealed class PassiveValue
        {
            public PassiveValue(int value) => Value = value;

            public int Value { get; }
        }

        public sealed class RightComparable(int value) : IComparable
        {
            public int CompareTo(object? other) =>
                value.CompareTo(((PassiveValue)other!).Value);
        }

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => SearchPrimitiveVector(),
                1 => SearchValueTypeVector(),
                2 => BinarySearchVectors(),
                3 => ReverseVectors(),
                4 => SortPrimitiveRange(),
                5 => SortValueTypeVector(),
                6 => CompareBoxedEnumValues(),
                7 => SearchEnumVector(),
                8 => SortEnumVector(),
                9 => SortKeysAndDifferentlyTypedItems(),
                10 => SortWithNonGenericComparer(),
                11 => ConstrainedCopyExactAndOverlapping(),
                12 => ConstrainedCopySafeCovariance(),
                13 => ConstrainedCopyFailureIsAtomic(),
                14 => ValidationAndComparerFailuresMatch(),
                _ => DefaultComparerUsesRightComparableFallback(),
            };

            public static int Trace() => 0;

            private static int SearchPrimitiveVector()
            {
                Array values = new[] { 1, 2, 3, 2 };
                return Array.IndexOf(values, 2) == 1 &&
                    Array.IndexOf(values, 2, 2, 2) == 3 &&
                    Array.IndexOf(values, 9) == -1 &&
                    Array.LastIndexOf(values, 2) == 3 &&
                    Array.LastIndexOf(values, 2, 2, 2) == 1 &&
                    Array.LastIndexOf(Array.Empty<int>(), 1) == -1 &&
                    Array.LastIndexOf(
                        (Array)Array.Empty<int>(),
                        1,
                        int.MaxValue,
                        int.MinValue) == -1
                    ? 42
                    : -1;
            }

            private static int SearchValueTypeVector()
            {
                Array values = new[] { new Point(1), new Point(2), new Point(3) };
                return Array.IndexOf(values, new Point(2)) == 1 &&
                    Array.LastIndexOf(values, new Point(3)) == 2 &&
                    Array.IndexOf(values, null) == -1
                    ? 42
                    : -2;
            }

            private static int BinarySearchVectors()
            {
                Array integers = new[] { 1, 3, 5, 7 };
                Array points = new[] { new Point(1), new Point(3), new Point(5) };
                return Array.BinarySearch(integers, 5) == 2 &&
                    Array.BinarySearch(integers, 4) == ~2 &&
                    Array.BinarySearch(points, new Point(3)) == 1
                    ? 42
                    : -3;
            }

            private static int ReverseVectors()
            {
                Array integers = new[] { 1, 2, 3, 4 };
                Array.Reverse(integers, 1, 3);
                var values = (int[])integers;
                Array points = new[] { new Point(1), new Point(2), new Point(3) };
                Array.Reverse(points);
                var pointValues = (Point[])points;
                return Matches(values, 1, 4, 3, 2) &&
                    pointValues[0].Value == 3 && pointValues[1].Value == 2 &&
                    pointValues[2].Value == 1
                    ? 42
                    : -4;
            }

            private static int SortPrimitiveRange()
            {
                Array values = new[] { 9, 4, 3, 2, 8 };
                Array.Sort(values, 1, 3);
                return Matches((int[])values, 9, 2, 3, 4, 8) ? 42 : -5;
            }

            private static int SortValueTypeVector()
            {
                Array points = new[] { new Point(3), new Point(1), new Point(2) };
                Array.Sort(points);
                var pointValues = (Point[])points;
                return pointValues[0].Value == 1 && pointValues[1].Value == 2 &&
                    pointValues[2].Value == 3
                    ? 42
                    : -6;
            }

            private static int SortEnumVector()
            {
                Array markers = new[] { Marker.Three, Marker.One, Marker.Two };
                Array.Sort(markers);
                var markerValues = (Marker[])markers;
                return markerValues[0] == Marker.One &&
                    markerValues[1] == Marker.Two &&
                    markerValues[2] == Marker.Three
                    ? 42
                    : -9;
            }

            private static int CompareBoxedEnumValues()
            {
                IComparable left = Marker.Three;
                object right = Marker.One;
                return left.CompareTo(right) > 0 ? 42 : -7;
            }

            private static int SearchEnumVector()
            {
                Array markers = new[] { Marker.Three, Marker.One, Marker.Two };
                return Array.IndexOf(markers, Marker.One) == 1 &&
                    Array.LastIndexOf(markers, Marker.Two) == 2
                    ? 42
                    : -8;
            }

            private static int SortKeysAndDifferentlyTypedItems()
            {
                Array keys = new[] { 3, 1, 2 };
                Array items = new[] { "three", "one", "two" };
                Array.Sort(keys, items);
                var keyValues = (int[])keys;
                var itemValues = (string[])items;
                return Matches(keyValues, 1, 2, 3) &&
                    itemValues[0] == "one" && itemValues[1] == "two" &&
                    itemValues[2] == "three"
                    ? 42
                    : -10;
            }

            private static int SortWithNonGenericComparer()
            {
                Array values = new[] { 1, 3, 2 };
                Array.Sort(values, new DescendingComparer());
                return Matches((int[])values, 3, 2, 1) ? 42 : -11;
            }

            private static int ConstrainedCopyExactAndOverlapping()
            {
                Array values = new[] { 1, 2, 3, 4 };
                Array.ConstrainedCopy(values, 0, values, 1, 3);
                return Matches((int[])values, 1, 1, 2, 3) ? 42 : -12;
            }

            private static int ConstrainedCopySafeCovariance()
            {
                Array source = new[] { "one", "two" };
                Array destination = new object[] { "x", "y" };
                Array.ConstrainedCopy(source, 0, destination, 0, 2);
                var values = (object[])destination;
                return values[0] is string first && first == "one" &&
                    values[1] is string second && second == "two"
                    ? 42
                    : -13;
            }

            private static int ConstrainedCopyFailureIsAtomic()
            {
                Array source = new object[] { "valid", new object() };
                Array destination = new[] { "left", "right" };
                var rejected = false;
                try
                {
                    Array.ConstrainedCopy(source, 0, destination, 0, 2);
                }
                catch (ArrayTypeMismatchException)
                {
                    rejected = true;
                }

                var values = (string[])destination;
                return rejected && values[0] == "left" && values[1] == "right"
                    ? 42
                    : -14;
            }

            private static int ValidationAndComparerFailuresMatch()
            {
                var rankRejected = false;
                var comparerWrapped = false;
                var binaryRangePrecedesRank = false;
                var reverseRangePrecedesRank = false;
                var indexRankPrecedesRange = false;
                var sortRankPrecedesRange = false;
                try
                {
                    Array.Reverse(new int[1, 1]);
                }
                catch (RankException)
                {
                    rankRejected = true;
                }
                try
                {
                    Array.BinarySearch(
                        (Array)new[] { 1, 2 },
                        1,
                        new ThrowingComparer());
                }
                catch (InvalidOperationException exception)
                {
                    comparerWrapped = exception.InnerException is ArgumentException;
                }

                try
                {
                    Array.BinarySearch((Array)new int[1, 1], -1, 0, 0);
                }
                catch (ArgumentOutOfRangeException)
                {
                    binaryRangePrecedesRank = true;
                }

                try
                {
                    Array.Reverse(new int[1, 1], -1, 0);
                }
                catch (ArgumentOutOfRangeException)
                {
                    reverseRangePrecedesRank = true;
                }

                try
                {
                    Array.IndexOf(new int[1, 1], 0, -1, 0);
                }
                catch (RankException)
                {
                    indexRankPrecedesRange = true;
                }

                try
                {
                    Array.Sort(new int[1, 1], -1, 0);
                }
                catch (RankException)
                {
                    sortRankPrecedesRange = true;
                }

                return rankRejected && comparerWrapped &&
                    binaryRangePrecedesRank && reverseRangePrecedesRank &&
                    indexRankPrecedesRange && sortRankPrecedesRange
                    ? 42
                    : -15;
            }

            private static int DefaultComparerUsesRightComparableFallback()
            {
                Array values = new object[] { new PassiveValue(7) };
                return Array.BinarySearch(values, new RightComparable(7)) == 0
                    ? 42
                    : -16;
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
