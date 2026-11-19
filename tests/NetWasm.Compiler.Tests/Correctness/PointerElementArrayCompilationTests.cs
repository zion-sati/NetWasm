// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

// Arrays whose elements are pointers or function pointers. Such an element type need be the type of no local,
// parameter or field (void* and the function pointers here are not), so the array alone gives it a value layout.
// Without one the runtime takes the elements for references: it refuses to allocate the array where it checks its
// contract, and where it does not, a copy reads each element as an object. One- and two-dimensional and jagged
// arrays are allocated, written, read, copied, cloned and cleared, on the linked runtime as well as the default.
public sealed class PointerElementArrayCompilationTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ArraysOfPointersAndFunctionPointersBehaveAsDesktopDoes() =>
        runner.Run(new("PointerElementArrays", "PointerElementArrays", Source, [0, 1, 2, 7])
        {
            AllowUnsafe = true,
            ReportAllMismatches = true,
        });

    [Theory]
    [InlineData("Debug-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm32-Direct-Linked")]
    [InlineData("Release-Wasm64-Direct-Linked")]
    [InlineData("Release-Wasm32-Optimized-Linked")]
    public void ArraysOfPointersAndFunctionPointersRunOnTheLinkedRuntime(string cell) =>
        runner.Run(new("PointerElementArrays", "PointerElementArrays", Source, [0, 1, 2, 7])
        {
            AllowUnsafe = true,
            ReportAllMismatches = true,
            Matrix = new(CorpusMatrixProfile.Extended, cell, CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        namespace PointerElementArrays;

        public static unsafe class EntryPoint
        {
            private static int _trace;
            private static readonly int[] s_numbers = { 3, 5, 7, 11 };

            public static int Trace() => _trace;

            private static int Add(int value) => value + 1;
            private static int Twice(int value) => value * 2;
            private static int Negate(int value) => -value;

            public static int Run(int input)
            {
                var hash = 17;
                hash = Mix(hash, Functions(input));
                hash = Mix(hash, Pointers(input));
                hash = Mix(hash, PointersToPointers());
                hash = Mix(hash, Rectangular(input));
                hash = Mix(hash, Jagged(input));
                _trace = hash;
                return hash;
            }

            private static int Mix(int hash, int value) => unchecked(hash * 31 + value);

            // Function pointers: stored, called through, copied, cloned, cleared.
            private static int Functions(int input)
            {
                var table = new delegate*<int, int>[5];
                table[0] = &Add;
                table[1] = &Twice;
                table[2] = &Negate;
                var copy = new delegate*<int, int>[5];
                Array.Copy(table, copy, table.Length);
                var hash = Mix(table.Length, copy[input % 3](input + 5));
                hash = Mix(hash, ((delegate*<int, int>[])table.Clone())[(input + 1) % 3](input));
                hash = Mix(hash, table[3] == null ? 1 : 0);
                hash = Mix(hash, copy[1](7) == table[1](7) ? 1 : 0);
                hash = Mix(hash, copy[0](7) == table[1](7) ? 1 : 0);
                Array.Copy(table, 0, copy, 2, 3);
                hash = Mix(hash, copy[2](input) + copy[4](input));
                Array.Clear(copy, 0, 2);
                hash = Mix(hash, copy[0] == null && copy[1] == null && copy[2] != null ? 1 : 0);
                table.CopyTo(copy, 0);
                hash = Mix(hash, copy[2](input + 1));
                return Mix(hash, table.Rank);
            }

            // Pointers to elements of a pinned array: stored, read through, copied, cloned, cleared.
            private static int Pointers(int input)
            {
                fixed (int* numbers = s_numbers)
                {
                    var pointers = new int*[4];
                    pointers[0] = numbers + 3;
                    pointers[1] = numbers + 2;
                    pointers[2] = numbers + (input & 1);
                    var copy = new int*[6];
                    Array.Copy(pointers, 0, copy, 1, 4);
                    var hash = Mix(pointers.Length, *copy[1] + *copy[2] * 2 + *copy[3] * 3);
                    hash = Mix(hash, copy[0] == null && copy[4] == null ? 1 : 0);
                    hash = Mix(hash, (int)(copy[1] - copy[2]));
                    hash = Mix(hash, *((int*[])pointers.Clone())[1]);
                    Array.Clear(pointers, 1, 2);
                    hash = Mix(hash, pointers[0] != null && pointers[1] == null && pointers[2] == null ? 1 : 0);
                    var untyped = new void*[2];
                    untyped[1] = numbers + 1;
                    hash = Mix(hash, *(int*)untyped[1]);
                    return Mix(hash, untyped[0] == null ? 1 : 0);
                }
            }

            private static int PointersToPointers()
            {
                fixed (int* numbers = s_numbers)
                {
                    var first = new int*[2];
                    first[1] = numbers + 2;
                    fixed (int** inner = first)
                    {
                        var outer = new int**[3];
                        outer[2] = inner;
                        return Mix(outer.Length, *outer[2][1]) + (outer[0] == null ? 1 : 0);
                    }
                }
            }

            // Two dimensions, of function pointers and of pointers.
            private static int Rectangular(int input)
            {
                var grid = new delegate*<int, int>[2, 3];
                grid[0, 0] = &Add;
                grid[1, 2] = &Twice;
                var hash = Mix(grid.Length, grid[1, 2](grid[0, 0](input)));
                hash = Mix(hash, grid[0, 1] == null ? 1 : 0);
                hash = Mix(hash, grid.GetLength(1) * 10 + grid.Rank);
                var clone = (delegate*<int, int>[,])grid.Clone();
                hash = Mix(hash, clone[0, 0](input) + (clone[1, 1] == null ? 1 : 0));
                fixed (int* numbers = s_numbers)
                {
                    var pointers = new int*[2, 2];
                    pointers[1, 0] = numbers + 3;
                    pointers[0, 1] = numbers + (input & 1);
                    hash = Mix(hash, *pointers[1, 0] - *pointers[0, 1]);
                    Array.Clear(pointers, 0, pointers.Length);
                    hash = Mix(hash, pointers[1, 0] == null ? 1 : 0);
                }
                return hash;
            }

            // Arrays of arrays of them: the outer array holds references, the inner ones values.
            private static int Jagged(int input)
            {
                var rows = new delegate*<int, int>[3][];
                rows[0] = new delegate*<int, int>[] { &Add, &Twice };
                rows[2] = new delegate*<int, int>[] { &Negate };
                var copy = new delegate*<int, int>[3][];
                Array.Copy(rows, copy, 3);
                var hash = Mix(copy.Length, copy[0][input & 1](input) + copy[2][0](input));
                hash = Mix(hash, copy[1] == null ? 1 : 0);
                return Mix(hash, ReferenceEquals(copy[0], rows[0]) ? 1 : 0);
            }
        }
        """;
}
