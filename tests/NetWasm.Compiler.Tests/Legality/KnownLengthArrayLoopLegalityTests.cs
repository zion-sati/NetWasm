using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A fill or copy loop over local arrays runs without checks only when each
// array is a local stored once from a new array of a constant length, ahead
// of the method's first branch and protected region and never addressed,
// and every iteration lies within it. Each method breaks one condition, and
// the loop must write and throw where desktop .NET does: a fill past the
// known length, the local stored again, replaced through its address,
// stored after a branch, a length that is not a constant, a counter that
// starts below zero, a protected region first, a copy past the shorter
// array, a constant end one past the length, and a start past the end.
public sealed class KnownLengthArrayLoopLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private static int Hash(char[] values)
            {
                var hash = 17;
                foreach (var value in values) hash = unchecked(hash * 31 + value);
                return hash;
            }

            private static int Guard(Func<int> body)
            {
                try { return body(); }
                catch (IndexOutOfRangeException) { return -1; }
            }

            // A fill that runs past the known length writes up to it, then throws.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int PastTheEnd(int input, out char[] seen)
            {
                var chars = new char[16];
                seen = chars;
                for (var i = 0; i < 16 + input % 3; i++) chars[i] = (char)('a' + i);
                return Hash(chars);
            }

            // The local is stored again before the loop.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Restored(int input)
            {
                var chars = new char[16];
                chars = new char[8 + input % 9];
                for (var i = 0; i < 16; i++) chars[i] = 'x';
                return Hash(chars);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Replace(ref char[] chars, int input) => chars = new char[10 + input % 8];

            // A callee replaces the array through its address.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReplacedByReference(int input)
            {
                var chars = new char[16];
                Replace(ref chars, input);
                for (var i = 0; i < 16; i++) chars[i] = 'y';
                return Hash(chars);
            }

            // The array is stored only after a branch.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int AfterABranch(int input)
            {
                char[] chars;
                if (input % 2 == 0) chars = new char[16]; else chars = new char[12];
                for (var i = 0; i < 16; i++) chars[i] = 'z';
                return Hash(chars);
            }

            // The length is not a constant.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int VariableLength(int input)
            {
                var chars = new char[14 + input % 4];
                for (var i = 0; i < 16; i++) chars[i] = 'v';
                return Hash(chars);
            }

            // The counter starts below zero.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int NegativeStart()
            {
                var chars = new char[16];
                for (var i = -1; i < 16; i++) chars[i] = 'n';
                return Hash(chars);
            }

            // A protected region comes before the array's store.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int RegionFirst(int input)
            {
                var size = 16;
                try { size = 16 / (input % 3); } catch (DivideByZeroException) { size = 9; }
                var chars = new char[16];
                for (var i = 0; i < size; i++) chars[i] = 'r';
                return Hash(chars);
            }

            // A copy between known-length arrays that runs past the shorter one.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int CopyPastShorter(int input, out char[] seen)
            {
                var source = new char[16];
                for (var i = 0; i < 16; i++) source[i] = (char)('A' + i);
                var target = new char[12];
                seen = target;
                for (var i = 0; i < 12 + input % 5; i++) target[i] = source[i];
                return Hash(target);
            }

            // A constant end one past the known length.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ConstantPastEnd(out char[] seen)
            {
                var chars = new char[16];
                seen = chars;
                for (var i = 0; i < 17; i++) chars[i] = 'p';
                return Hash(chars);
            }

            // A start past the end runs no iteration.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int StartPastEnd()
            {
                var chars = new char[16];
                for (var i = 20; i < 16; i++) chars[i] = 's';
                return Hash(chars);
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                char[] seen = [];
                switch (which)
                {
                    case 0: return Guard(() => PastTheEnd(input, out seen)) * 1000 + Hash(seen) % 1000;
                    case 1: return Guard(() => Restored(input));
                    case 2: return Guard(() => ReplacedByReference(input));
                    case 3: return Guard(() => AfterABranch(input));
                    case 4: return Guard(() => VariableLength(input));
                    case 5: return Guard(NegativeStart);
                    case 6: return Guard(() => RegionFirst(input));
                    case 7: return Guard(() => CopyPastShorter(input, out seen)) * 1000 + Hash(seen) % 1000;
                    case 8: return Guard(() => ConstantPastEnd(out seen)) * 1000 + Hash(seen) % 1000;
                    case 9: return Guard(StartPastEnd);
                    default: return -9;
                }
            }
        }

        """;

    // As desktop .NET 11 computes them; -1 marks IndexOutOfRange.
    private static readonly (int Input, int Expected)[] Cases = [(0, 190052113), (1, -1663), (2, -1663), (3, 190052113), (6, 190052113), (100, -1), (101, -1), (102, -1), (103, -1), (106, -1), (200, -1), (201, -1), (202, -1), (203, -1), (206, 1835816721), (300, 1021933585), (301, -1), (302, 1021933585), (303, -1), (306, 1021933585), (400, -1), (401, -1), (402, -17501167), (403, -542536177), (406, -17501167), (500, -1), (501, -1), (502, -1), (503, -1), (506, -1), (600, 1769312863), (601, -1056935919), (602, 657430289), (603, 1769312863), (606, 1769312863), (700, 2046299711), (701, -385), (702, -385), (703, -385), (706, -385), (800, -647), (801, -647), (802, -647), (803, -647), (806, -647), (900, 1531428369), (901, 1531428369), (902, 1531428369), (903, 1531428369), (906, 1531428369)];

    [Theory]
    [InlineData(ManagedArrayLoopIdioms.Auto)]
    [InlineData(ManagedArrayLoopIdioms.Enabled)]
    [InlineData(ManagedArrayLoopIdioms.Disabled)]
    public void FillsAndCopiesUncheckedOnlyWhereTheLengthIsKnown(ManagedArrayLoopIdioms idioms) =>
        LegalityCases.Run($"KnownLengthArrayLoopLegality{idioms}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(ArrayLoopIdioms: idioms));
}
