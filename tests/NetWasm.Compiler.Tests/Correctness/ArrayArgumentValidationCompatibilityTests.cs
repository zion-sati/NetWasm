// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Every Array method that takes an index, start, count or length, given each kind of bad argument, throws what desktop
// throws, with the parameter name desktop names: Array's validation is upstream's. Before, a range error threw without
// a parameter name, the overloads without an index started at 0 instead of an array's lower bound, and Array.Clear<T>
// and Array.Copy<T>, which .NET does not have, took calls that bind Array.Clear(Array, int, int) and
// Array.Copy(Array, int, Array, int, int) on desktop (Clear<T> threw ArgumentOutOfRangeException where desktop throws
// IndexOutOfRangeException, and cleared an element at a time).
public sealed class ArrayArgumentValidationCompatibilityTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ArrayArgumentErrorsMatchDesktop() => runner.Run(new CorpusFixture(
        "ArrayArgumentValidation",
        "NetWasm.Correctness.ArrayArguments",
        Source,
        [0, 1, 2, 3, 4, 5, 6])
    {
        OracleMode = OracleMode.SameSource,
        SameSourceReason = "Array's argument exceptions are ordinary CLR contracts.",
    });

    // The overloads without an index start at an array's lower bound, on arrays Array.CreateInstance makes with lower
    // bounds.
    [Fact]
    public void LowerBoundedArraysMatchDesktop() => runner.Run(new CorpusFixture(
        "ArrayArgumentValidationLowerBounds",
        "NetWasm.Correctness.ArrayArguments",
        Source,
        [7])
    {
        OracleMode = OracleMode.SameSource,
        SameSourceReason = "Array's argument exceptions are ordinary CLR contracts.",
    });

    private const string Source = """
        using System;

        namespace NetWasm.Correctness.ArrayArguments;

        public static class EntryPoint
        {
            private static int _trace;

            public static int Trace() => _trace;

            // FNV-1a over the outcome's text: string.GetHashCode differs from run to run on desktop.
            private static void Check(string name, Action action)
            {
                string result;
                try { action(); result = "ok"; }
                catch (IndexOutOfRangeException) { result = "IndexOutOfRange"; }
                catch (ArgumentNullException exception) { result = "ArgumentNull:" + exception.ParamName; }
                catch (ArgumentOutOfRangeException exception) { result = "ArgumentOutOfRange:" + exception.ParamName; }
                catch (RankException) { result = "Rank"; }
                catch (ArgumentException exception) { result = "Argument:" + exception.ParamName; }
                catch (Exception) { result = "other"; }
                foreach (var character in name + "=" + result + ";")
                {
                    _trace = unchecked((_trace ^ character) * 16777619);
                }
            }

            // A value, or the exception, so the overloads that default to an array's lower bound are checked by what
            // they return as well as by what they throw.
            private static void Value(string name, Func<int> value)
            {
                var result = 0;
                Check(name, () => { result = value(); });
                foreach (var character in name + "#" + result + ";")
                {
                    _trace = unchecked((_trace ^ character) * 16777619);
                }
            }

            // A one-dimensional array whose indices run from 10 to 13 (int[*], not int[]).
            private static void LowerBounds()
            {
                var lb = Array.CreateInstance(typeof(int), new[] { 4 }, new[] { 10 });
                for (var index = 10; index < 14; index++) lb.SetValue(index * 2 - 18, index);
                var other = Array.CreateInstance(typeof(int), new[] { 4 }, new[] { 11 });
                var plain = new int[4];
                Value("lb IndexOf", () => Array.IndexOf(lb, 4));
                Value("lb IndexOf missing", () => Array.IndexOf(lb, 99));
                Value("lb IndexOf start", () => Array.IndexOf(lb, 6, 12));
                Value("lb IndexOf start below", () => Array.IndexOf(lb, 6, 9));
                Value("lb IndexOf start at end", () => Array.IndexOf(lb, 6, 14));
                Value("lb IndexOf start past end", () => Array.IndexOf(lb, 6, 15));
                Value("lb IndexOf count", () => Array.IndexOf(lb, 6, 11, 4));
                Value("lb LastIndexOf", () => Array.LastIndexOf(lb, 4));
                Value("lb LastIndexOf missing", () => Array.LastIndexOf(lb, 99));
                Value("lb LastIndexOf start", () => Array.LastIndexOf(lb, 4, 12));
                Value("lb LastIndexOf start below", () => Array.LastIndexOf(lb, 4, 9));
                Value("lb LastIndexOf start past end", () => Array.LastIndexOf(lb, 4, 14));
                Value("lb LastIndexOf count", () => Array.LastIndexOf(lb, 4, 11, 3));
                Value("lb BinarySearch", () => Array.BinarySearch(lb, 4));
                Value("lb BinarySearch missing", () => Array.BinarySearch(lb, 5));
                Value("lb BinarySearch index below", () => Array.BinarySearch(lb, 9, 2, 4));
                Value("lb BinarySearch range", () => Array.BinarySearch(lb, 12, 3, 4));
                Value("lb Reverse", () => { Array.Reverse(lb); return (int)lb.GetValue(10)!; });
                Value("lb Reverse range", () => { Array.Reverse(lb, 11, 2); return (int)lb.GetValue(11)!; });
                Value("lb Reverse index below", () => { Array.Reverse(lb, 9, 2); return 0; });
                Value("lb Reverse range past end", () => { Array.Reverse(lb, 12, 3); return 0; });
                Value("lb Sort", () => { Array.Sort(lb); return (int)lb.GetValue(13)!; });
                Value("lb Sort range past end", () => { Array.Sort(lb, 10, 5); return 0; });
                Value("lb Sort bounds differ", () => { Array.Sort(lb, other); return 0; });
                Value("lb Sort with items", () => { Array.Sort(lb, (Array)lb.Clone()); return (int)lb.GetValue(10)!; });
                Value("lb Clear", () => { Array.Clear(lb, 12, 2); return (int)lb.GetValue(12)!; });
                Value("lb Clear below", () => { Array.Clear(lb, 9, 1); return 0; });
                Value("lb Clear past end", () => { Array.Clear(lb, 12, 3); return 0; });
                Value("lb Copy", () => { Array.Copy(lb, 10, plain, 0, 4); return plain[1]; });
                Value("lb Copy below", () => { Array.Copy(lb, 9, plain, 0, 1); return 0; });
                Value("lb Copy past end", () => { Array.Copy(lb, 12, plain, 0, 3); return 0; });
                Value("lb Copy into", () => { Array.Copy(plain, 0, lb, 11, 3); return (int)lb.GetValue(13)!; });
                Value("lb ConstrainedCopy below", () => { Array.ConstrainedCopy(plain, 0, lb, 9, 1); return 0; });
                Value("lb GetValue", () => (int)lb.GetValue(13)!);
                Value("lb GetValue below", () => (int)lb.GetValue(9)!);
                Value("lb GetLowerBound", () => lb.GetLowerBound(0) * 100 + lb.GetUpperBound(0));
            }

            public static int Run(int input)
            {
                _trace = unchecked((int)2166136261);
                if (input == 7)
                {
                    LowerBounds();
                    return _trace;
                }
                var a = new int[] { 5, 3, 9, 1 };
                var b = new int[2];
                Array untyped = a;
                Predicate<int> even = value => value % 2 == 0;
                if (input == 6)
                {
                    int[]? none = null;
                    var matrix = new int[2, 2];
                    Check("Clear null", () => Array.Clear(none!, 0, 0));
                    Check("Copy null", () => Array.Copy(none!, 0, b, 0, 0));
                    Check("Copy rank", () => Array.Copy(matrix, 0, b, 0, 1));
                    Check("BinarySearch null", () => Array.BinarySearch(none!, 0, 0, 3));
                    Check("Fill null", () => Array.Fill(none!, 0, 0, 0));
                    Check("IndexOf null", () => Array.IndexOf(none!, 9, 0, 0));
                    Check("Reverse null", () => Array.Reverse(none!, 0, 0));
                    Check("Sort null", () => Array.Sort(none!, 0, 0));
                    Check("SortComparison null", () => Array.Sort(a, (Comparison<int>)null!));
                    Check("FindIndex null match", () => Array.FindIndex(a, 0, 1, null!));
                    Check("FindIndex null match and range", () => Array.FindIndex(a, 9, 1, null!));
                    Check("ConvertAll null", () => Array.ConvertAll<int, int>(a, null!));
                    Check("ForEach null", () => Array.ForEach(a, null!));
                    Check("AsReadOnly null", () => Array.AsReadOnly(none!));
                    Check("Resize negative", () => { var copy = (int[])a.Clone(); Array.Resize(ref copy, -1); });
                    Check("Reverse rank", () => Array.Reverse(matrix));
                    Check("Sort rank", () => Array.Sort(matrix));
                    Check("IndexOf rank", () => Array.IndexOf(matrix, 1));
                    Check("LastIndexOf empty", () => Array.LastIndexOf(Array.Empty<int>(), 1, 0));
                    Check("FindLastIndex empty", () => Array.FindLastIndex(Array.Empty<int>(), 0, even));
                    return _trace;
                }
                var (start, count) = input switch
                {
                    0 => (-1, 1),
                    1 => (0, -1),
                    2 => (3, 2),
                    3 => (5, 0),
                    4 => (4, 0),
                    _ => (0, 5),
                };
                Check("Clear<T>", () => Array.Clear(a, start, count));
                Check("Clear", () => Array.Clear(untyped, start, count));
                Check("Copy<T>", () => Array.Copy(a, start, b, 0, count));
                Check("CopyDest<T>", () => Array.Copy(a, 0, b, start, count));
                Check("ConstrainedCopy", () => Array.ConstrainedCopy(a, start, b, 0, count));
                Check("BinarySearch<T>", () => Array.BinarySearch(a, start, count, 3));
                Check("BinarySearch", () => Array.BinarySearch(untyped, start, count, 3));
                Check("Fill<T>", () => Array.Fill(a, 0, start, count));
                Check("IndexOf<T>", () => Array.IndexOf(a, 9, start, count));
                Check("IndexOf", () => Array.IndexOf(untyped, 9, start, count));
                Check("LastIndexOf<T>", () => Array.LastIndexOf(a, 9, start, count));
                Check("LastIndexOf", () => Array.LastIndexOf(untyped, 9, start, count));
                Check("Reverse<T>", () => Array.Reverse(a, start, count));
                Check("Reverse", () => Array.Reverse(untyped, start, count));
                Check("Sort<T>", () => Array.Sort(a, start, count));
                Check("Sort", () => Array.Sort(untyped, start, count));
                Check("SortPair<T>", () => Array.Sort(a, (int[])a.Clone(), start, count));
                Check("FindIndex", () => Array.FindIndex(a, start, count, even));
                Check("FindLastIndex", () => Array.FindLastIndex(a, start, count, even));
                Check("IndexOfStart<T>", () => Array.IndexOf(a, 9, start));
                Check("LastIndexOfStart<T>", () => Array.LastIndexOf(a, 9, start));
                Check("FindIndexStart", () => Array.FindIndex(a, start, even));
                Check("FindLastIndexStart", () => Array.FindLastIndex(a, start, even));
                Check("CopyTo", () => a.CopyTo(b, start));
                Check("GetValue", () => untyped.GetValue(start));
                Check("SetValue", () => untyped.SetValue(1, start));
                return _trace;
            }
        }
        """;
}
