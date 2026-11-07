using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A rectangular array access skips its null check, and the check of each
// dimension, only while an earlier access to the same array in its block
// checked them and the slots still hold what it checked. Each method breaks one
// condition, and the checks must still throw where desktop .NET throws: the row
// changed between two reads, a second read whose row or column alone is new and
// out of range, the array replaced by a smaller one or by null, the column
// changed through its address, the same indices in swapped dimensions, and
// another array with the same indices, the column changed on a branch, the array
// replaced through its address, through a ref to it or through a ref chosen at
// run time, and a store after the array became null.
// MDAddArray2's update and three dimensions keep desktop's results.
public sealed class RectangularArrayAccessLegalityTests
{
    private const string Source = """

        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(ref int value) => value += 1;

            private static int Guard(Func<int> body)
            {
                try { return body(); }
                catch (IndexOutOfRangeException) { return -1; }
                catch (NullReferenceException) { return -2; }
            }

            // The row index changes between two reads of a row.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int RowChanged(int[,] grid, int i, int k, int input)
            {
                var result = grid[i, k];
                i = input;
                return result * 10 + grid[i, k];
            }

            // The second read's row is out of range though its column was checked.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SecondRow(int[,] grid, int j, int k) => grid[1, k] * 10 + grid[j, k];

            // The second read's column is out of range though its row was checked.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int SecondColumn(int[,] grid, int i, int k) => grid[i, 1] * 10 + grid[i, k];

            // The array changes to a smaller one between two reads of one element.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArrayReplaced(int[,] grid, int[,] small, int i, int k)
            {
                var result = grid[i, k];
                grid = small;
                return result * 10 + grid[i, k];
            }

            // The array becomes null between two reads.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArrayNulled(int[,] grid, int i, int k, int input)
            {
                var result = grid[i, k];
                if (input % 2 == 1) grid = null!;
                return result * 10 + grid[i, k];
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Shrink(ref int[,] grid, int[,] small) => grid = small;

            // The array is replaced through its address between two reads of one element.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArrayByAddress(int[,] grid, int[,] small, int i, int k)
            {
                var result = grid[i, k];
                Shrink(ref grid, small);
                return result * 10 + grid[i, k];
            }

            // The array is replaced through a ref to it, with no call, between two reads of one element.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArrayByAlias(int[,] grid, int[,] small, int i, int k)
            {
                ref var alias = ref grid;
                var result = grid[i, k];
                alias = small;
                return result * 10 + grid[i, k];
            }

            // The array is replaced through a ref chosen at run time, with no call, between two reads.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ArrayByChosenRef(int[,] grid, int[,] other, int[,] small, int i, int k, bool pick)
            {
                ref var alias = ref pick ? ref grid : ref other;
                var result = grid[i, k];
                alias = small;
                return result * 10 + grid[i, k];
            }

            // The array becomes null between two stores.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StoreAfterNulled(int[,] grid, int i, int k, int input)
            {
                grid[i, k] = input;
                if (input % 2 == 1) grid = null!;
                grid[i, k] = input + 1;
                return grid[i, k];
            }

            // The column is changed through its address between two reads.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ColumnByAddress(int[,] grid, int i, int k)
            {
                var result = grid[i, k];
                Bump(ref k);
                return result * 10 + grid[i, k];
            }

            // The same indices in swapped dimensions of a grid that is not square.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Swapped(int[,] grid, int i, int k) => grid[i, k] * 10 + grid[k, i];

            // Another array of a smaller shape with the same indices.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OtherArray(int[,] grid, int[,] small, int i, int k) => grid[i, k] * 10 + small[i, k];

            // MDAddArray2's update: the store repeats the second read's indices.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AddRows(int[,] grid, int rows, int columns)
            {
                for (var i = 0; i < rows; i++)
                    for (var j = i + 1; j < rows; j++)
                        for (var k = 0; k < columns; k++)
                            grid[j, k] = grid[i, k] + grid[j, k];
                var hash = 0;
                foreach (var value in grid) hash = hash * 31 + value;
                return hash;
            }

            // The column changes on a branch between two reads, in another block.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ColumnOnBranch(int[,] grid, int i, int k, int input)
            {
                var result = grid[i, k];
                if (input % 2 == 0) k = input;
                return result * 10 + grid[i, k];
            }

            // Three dimensions: the third read shares two indices with the first.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Cube(int[,,] cube, int i, int j, int k) => cube[i, j, 0] + cube[i, 1, k] * 10 + cube[i, j, k] * 100;

            private static int[,] Grid(int rows, int columns)
            {
                var grid = new int[rows, columns];
                for (var i = 0; i < rows; i++)
                    for (var k = 0; k < columns; k++)
                        grid[i, k] = i * 7 + k + 1;
                return grid;
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                var grid = Grid(4, 3);
                var small = Grid(2, 2);
                var cube = new int[3, 3, 3];
                for (var x = 0; x < 3; x++) for (var y = 0; y < 3; y++) for (var z = 0; z < 3; z++) cube[x, y, z] = x * 9 + y * 3 + z;
                return which switch
                {
                    0 => Guard(() => RowChanged(grid, 1, 2, input)),
                    1 => Guard(() => SecondRow(grid, input, 2)),
                    2 => Guard(() => SecondColumn(grid, 2, input)),
                    3 => Guard(() => ArrayReplaced(grid, small, input % 4, 1)),
                    4 => Guard(() => ArrayNulled(grid, 2, 1, input)),
                    5 => Guard(() => ColumnByAddress(grid, 1, input % 3)),
                    6 => Guard(() => Swapped(grid, input, 2)),
                    7 => Guard(() => OtherArray(grid, small, 1, input % 3)),
                    8 => Guard(() => AddRows(Grid(4, 3), 4, input % 4)),
                    9 => Guard(() => ColumnOnBranch(grid, 2, 1, input)),
                    10 => Guard(() => Cube(cube, input % 4, 2, input % 3)),
                    11 => Guard(() => ArrayByAddress(grid, small, input % 4, 1)),
                    12 => Guard(() => StoreAfterNulled(Grid(4, 3), 1, 2, input)),
                    13 => Guard(() => ArrayByAlias(grid, small, input % 4, 1)),
                    14 => Guard(() => ArrayByChosenRef(grid, Grid(4, 3), small, input % 4, 1, input % 2 == 0)),
                    _ => -9,
                };
            }
        }

        """;

    // As desktop .NET 11 computes them; -1 and -2 are IndexOutOfRange and NullReference.
    private static readonly (int Input, int Expected)[] Cases =
        [(0, 103), (1, 110), (2, 117), (3, 124), (4, -1), (100, 103), (101, 110), (102, 117), (103, 124), (104, -1), (200, 175), (201, 176), (202, 177), (203, -1), (204, -1), (300, 22), (301, 99), (302, -1), (303, -1), (304, 22), (400, 176), (401, -2), (402, 176), (403, -2), (404, 176), (500, 89), (501, 100), (502, -1), (503, 89), (504, 100), (600, 45), (601, 116), (602, 187), (603, -1), (604, -1), (700, 88), (701, 99), (702, -1), (703, 88), (704, 99), (800, -2098859058), (801, 674978920), (802, -122360631), (803, -1754340179), (804, -2098859058), (900, 175), (901, 176), (902, 177), (903, 176), (904, -1), (1000, 636), (1001, 1745), (1002, 2854), (1003, -1), (1004, 746), (1100, 22), (1101, 99), (1102, -1), (1103, -1), (1104, 22), (1200, 1), (1201, -2), (1202, 3), (1203, -2), (1204, 5), (1300, 22), (1301, 99), (1302, -1), (1303, -1), (1304, 22), (1400, 22), (1401, 99), (1402, -1), (1403, 253), (1404, 22)];

    [Theory]
    [InlineData(ManagedArrayBoundsCheckElimination.Auto)]
    [InlineData(ManagedArrayBoundsCheckElimination.Enabled)]
    [InlineData(ManagedArrayBoundsCheckElimination.Disabled)]
    public void SkipsARectangularCheckOnlyWhileAnEarlierAccessProvesIt(ManagedArrayBoundsCheckElimination elimination) =>
        LegalityCases.Run($"RectangularArrayAccessLegality{elimination}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(ArrayBoundsCheckElimination: elimination));
}
