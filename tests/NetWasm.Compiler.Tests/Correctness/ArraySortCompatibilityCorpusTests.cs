namespace NetWasm.Compiler.Tests.Correctness;

public sealed class ArraySortCompatibilityCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void IntrosortMatchesDesktopAcrossAlgorithmBoundariesAndOverloads() =>
        runner.Run(new(
            "ArraySortCompatibilityCorpus",
            "NetWasm.Correctness.ArraySort",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12]));

    private const string Source = """
        using System;
        using System.Collections;
        using System.Collections.Generic;

        namespace NetWasm.Correctness.ArraySort;

        public sealed class DescendingComparer : IComparer<int>
        {
            public int Compare(int left, int right) => right.CompareTo(left);
        }

        public sealed class ThrowingComparer : IComparer<int>
        {
            public int Compare(int left, int right) => throw new PlannedException();
        }

        public sealed class InconsistentComparer : IComparer<int>
        {
            public int Compare(int left, int right) => -1;
        }

        public sealed class PlannedException : Exception
        {
        }

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => SortReverse(),
                1 => SortSeededValues(),
                2 => SortDuplicatesAcrossThreshold(),
                3 => SortSubrange(),
                4 => SortPairedArrays(),
                5 => SortWithCustomComparer(),
                6 => SortListAndSubrange(),
                7 => SortNullableAndNonGenericArrays(),
                8 => PreserveComparerFailure(),
                9 => SortWithComparisonDelegate(),
                10 => BinarySearchFoundAndMissingValues(),
                11 => SortFloatingPointNaNs(),
                _ => RejectInconsistentComparer(),
            };

            private static int SortReverse()
            {
                var values = new int[257];
                for (var index = 0; index < values.Length; index++)
                {
                    values[index] = values.Length - index;
                }
                Array.Sort(values);
                return Validate(values) ? Checksum(values) : -1;
            }

            private static int SortSeededValues()
            {
                var values = new int[1024];
                var state = 0x12345678U;
                for (var index = 0; index < values.Length; index++)
                {
                    state ^= state << 13;
                    state ^= state >> 17;
                    state ^= state << 5;
                    values[index] = (int)(state & 0x7fffffff);
                }
                Array.Sort(values);
                return Validate(values) ? Checksum(values) : -1;
            }

            private static int SortDuplicatesAcrossThreshold()
            {
                var values = new int[65];
                for (var index = 0; index < values.Length; index++)
                {
                    values[index] = (index * 17) % 9;
                }
                Array.Sort(values);
                return Validate(values) ? Checksum(values) : -1;
            }

            private static int SortSubrange()
            {
                var values = new[] { 900, 8, 7, 6, 5, 4, 3, 2, 1, -900 };
                Array.Sort(values, 1, 8);
                return values[0] * 1000000 + values[1] * 10000 +
                    values[8] * 100 + values[9];
            }

            private static int SortPairedArrays()
            {
                var keys = new[] { 5, 1, 4, 2, 3 };
                var values = new[] { 50, 10, 40, 20, 30 };
                Array.Sort(keys, values);
                var checksum = 0;
                for (var index = 0; index < keys.Length; index++)
                {
                    if (values[index] != keys[index] * 10)
                    {
                        return -1;
                    }
                    checksum = unchecked(checksum * 31 + keys[index]);
                }
                return checksum;
            }

            private static int SortWithCustomComparer()
            {
                var values = new int[64];
                for (var index = 0; index < values.Length; index++)
                {
                    values[index] = index;
                }
                Array.Sort(values, new DescendingComparer());
                for (var index = 1; index < values.Length; index++)
                {
                    if (values[index - 1] < values[index])
                    {
                        return -1;
                    }
                }
                return Checksum(values);
            }

            private static int SortListAndSubrange()
            {
                var values = new List<int>();
                for (var index = 80; index >= 1; index--)
                {
                    values.Add(index);
                }
                values.Sort();
                values.Reverse();
                values.Sort(10, 60, Comparer<int>.Default);
                var checksum = 0;
                for (var index = 0; index < values.Count; index++)
                {
                    checksum = unchecked(checksum * 31 + values[index]);
                }
                return checksum;
            }

            private static int SortNullableAndNonGenericArrays()
            {
                string?[] text = ["z", null, "a", "m"];
                Array.Sort(text);
                Array values = new object[] { 9, 2, 7, 1 };
                Array.Sort(values);
                return (text[0] is null ? 10000 : 0) + text[1]![0] * 100 +
                    (int)values.GetValue(0)! * 10 + (int)values.GetValue(3)!;
            }

            private static int PreserveComparerFailure()
            {
                try
                {
                    Array.Sort(new[] { 2, 1 }, new ThrowingComparer());
                    return -1;
                }
                catch (InvalidOperationException exception)
                {
                    return exception.InnerException is PlannedException ? 42 : -2;
                }
            }

            private static int SortWithComparisonDelegate()
            {
                var values = new[] { 3, 1, 4, 2 };
                Array.Sort(values, static (left, right) => right.CompareTo(left));
                return values[0] * 1000 + values[1] * 100 + values[2] * 10 + values[3];
            }

            private static int BinarySearchFoundAndMissingValues()
            {
                var values = new[] { -5, -1, 0, 4, 9, 12 };
                var found = Array.BinarySearch(values, 1, 4, 4, Comparer<int>.Default);
                var missing = Array.BinarySearch(values, 1, 4, 3, null);
                return found * 100 + missing;
            }

            private static int SortFloatingPointNaNs()
            {
                var doubles = new[] { 1.0, double.NaN, -2.0, double.NaN, 0.0 };
                var singles = new[] { 1.0f, float.NaN, -2.0f, float.NaN, 0.0f };
                Array.Sort(doubles);
                Array.Sort(singles);
                return double.IsNaN(doubles[0]) && double.IsNaN(doubles[1]) &&
                    doubles[2] == -2.0 && doubles[3] == 0.0 && doubles[4] == 1.0 &&
                    float.IsNaN(singles[0]) && float.IsNaN(singles[1]) &&
                    singles[2] == -2.0f && singles[3] == 0.0f && singles[4] == 1.0f
                        ? 42
                        : -1;
            }

            private static int RejectInconsistentComparer()
            {
                try
                {
                    Array.Sort(new int[64], new InconsistentComparer());
                    return -1;
                }
                catch (ArgumentException)
                {
                    return 42;
                }
            }

            private static bool Validate(int[] values)
            {
                for (var index = 1; index < values.Length; index++)
                {
                    if (values[index - 1] > values[index])
                    {
                        return false;
                    }
                }
                return true;
            }

            private static int Checksum(IReadOnlyList<int> values)
            {
                var checksum = 0;
                for (var index = 0; index < values.Count; index++)
                {
                    checksum = unchecked(checksum * 31 + values[index]);
                }
                return checksum;
            }

            public static int Trace() => 0;
        }
        """;
}
