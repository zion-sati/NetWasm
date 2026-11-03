using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Legality;

// A static readonly field reads as a constant only where its owner's static
// constructor stores that constant before running anything else and never
// stores or addresses it again, and the read still initializes the owner
// where the constructor does more. Each owner breaks one condition: a later
// store from a call, a later pass by reference, a constructor that fails, one
// with an effect the read must cause, constants and defaults of every width,
// a generic owner, a reference stored null and then an object, and a
// constructor that first stores another type's static, whose own
// constructor reads the field before it holds the constant. The
// rewrite takes no flag, so every preset gives desktop .NET's results.
public sealed class ConstantStaticFieldLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        using System.Threading;

        public static class EntryPoint
        {
            private enum Color : byte { Red = 1, Blue = 200 }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Twice(int value) => value * 2;

            // Stored as a constant first, then again from a call.
            private static class Reassigned
            {
                public static readonly int Value = 5;
                static Reassigned() { Value = Twice(Value); }
            }

            // Stored as a constant first, then passed by reference.
            private static class ByReference
            {
                public static readonly int Value = 3;
                static ByReference() { Interlocked.Increment(ref Value); }
            }

            // Stored as a constant first, then the constructor fails.
            private static class Failing
            {
                public static readonly int Value = 7;
                static Failing() { if (Twice(1) == 2) throw new InvalidOperationException(); }
            }

            private static int s_initialized;

            // Stored as a constant first, then the constructor has an effect.
            private static class Effect
            {
                public static readonly int Value = 9;
                static Effect() { s_initialized += 1; }
            }

            // Only constants and defaults, in every width.
            private static class Widths
            {
                public static readonly Color Paint = Color.Blue;
                public static readonly float Ratio = 1.5f;
                public static readonly long Big = 1L << 40;
                public static readonly byte Small = 250;
                public static readonly sbyte Negative = -5;
                public static readonly char Letter = 'k';
                public static readonly bool Flag = true;
                public static readonly int Zero;
                public static readonly object? Nothing;
                public static readonly float Unset;
                public static readonly double Missing;
            }

            // A generic owner's constant.
            private static class Generic<T>
            {
                public static readonly int Value = 11;
            }

            // A reference stored null first, then an object later.
            private static class Replaced
            {
                public static readonly string? Name = null;
                static Replaced() { Name = null; Name = new string('r', Twice(2)); }
            }

            // The constructor first stores another type's static, whose constructor reads this field early.
            private static class Owner
            {
                public static readonly int Value;
                static Owner() { Other.Flag = 1; Value = 5; }
            }

            private static class Other
            {
                public static int Flag;
                public static int Seen = -1;
                static Other() { Seen = Owner.Value; }
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                switch (which)
                {
                    case 0: return Reassigned.Value + input;
                    case 1: return ByReference.Value + input;
                    case 2:
                        try { return Failing.Value + input; }
                        // NetWasm throws the initializer's own exception, which desktop wraps.
                        catch (Exception) { return -1; }
                    case 3:
                        var before = s_initialized;
                        var value = Effect.Value;
                        return before * 1000 + s_initialized * 100 + value + input;
                    case 4:
                        return (int)Widths.Paint + (int)(Widths.Ratio * 10) + (int)(Widths.Big >> 30) + Widths.Small +
                            Widths.Negative + Widths.Letter + (Widths.Flag ? 1 : 0) + Widths.Zero + (Widths.Nothing is null ? 3 : 0) +
                            (int)(Widths.Unset * 7 + 1) + (int)(Widths.Missing - 1) + input;
                    case 5: return Generic<int>.Value * 10 + Generic<string>.Value + input;
                    case 6: return (Replaced.Name?.Length ?? -1) + input;
                    case 7: return Owner.Value * 100 + Other.Seen + input;
                    default: return -9;
                }
            }
        }

        """;

    // As desktop .NET 11 computes them, each input in a fresh process.
    private static readonly (int Input, int Expected)[] Cases = [(0, 10), (1, 11), (100, 4), (101, 5), (200, -1), (201, -1), (300, 109), (301, 110), (400, 1595), (401, 1596), (500, 121), (501, 122), (600, 4), (601, 5), (700, 500), (701, 501)];

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O0)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void ReadsAStaticReadonlyFieldAsAConstantOnlyWhenNothingElseCanSetIt(FinalWasmOptimization optimization) =>
        LegalityCases.Run($"ConstantStaticFieldLegality{optimization}", Source, Cases, optimization);
}
