using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A frame only one arm of a leading if needs is entered on that arm, with the root frame when no
// root lives outside it, and the arguments it holds are copied in where the arm begins. Each
// method breaks one condition, and each gives desktop .NET 11's result: the cold arm alone
// addressing an argument, the argument written before the branch, its address taken in the
// condition, in both arms, in the other arm alone or beside a local the framed arm addresses, a
// value-type argument, a reference argument
// replaced and collected, a root the hot arm or the condition holds across a collection, and the
// framed arm continuing past the if.
public sealed class FrameShrinkWrappingLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public sealed class Box(int value)
        {
            public int Value = value;
        }

        public struct Pair
        {
            public long A, B;
        }

        public static class EntryPoint
        {
            private static object? s_sink;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Bump(ref long value) => value = value * 7 + 3;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Swap(ref Box box) => box = new Box(box.Value + 100);

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Twist(ref Pair pair)
            {
                pair.A += pair.B;
                pair.B ^= 5;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Churn()
            {
                GC.Collect();
                for (var index = 0; index < 32; index++) s_sink = new Box(index);
                GC.Collect();
            }

            // Only the cold arm takes the argument's address: the argument is copied into its frame there.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long ColdArm(long value, bool cold)
            {
                if (cold)
                {
                    Bump(ref value);
                    return value;
                }
                return value + 1;
            }

            // The argument is written before the branch; the cold arm's copy starts from that value.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long WrittenBefore(long value, bool cold)
            {
                value = value * 3 - 1;
                if (cold)
                {
                    Bump(ref value);
                    return value;
                }
                return value;
            }

            // The condition takes the argument's address.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long AddressedInCondition(long value, bool cold)
            {
                Bump(ref value);
                if (cold)
                {
                    Bump(ref value);
                    return value;
                }
                return value - 2;
            }

            // Both arms take the argument's address.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long AddressedInBoth(long value, bool cold)
            {
                if (cold)
                {
                    Bump(ref value);
                    return value;
                }
                Bump(ref value);
                return -value;
            }

            // The hot arm takes the address; the cold arm only reads the argument.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long AddressedInOther(long value, bool cold)
            {
                if (cold)
                {
                    return value * 5;
                }
                Bump(ref value);
                return value;
            }

            // The cold arm needs the frame for a local's address; the hot arm takes the argument's.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long AddressedBesideAFramedLocal(long value, bool cold)
            {
                if (cold)
                {
                    var local = value * 9;
                    Bump(ref local);
                    return local;
                }
                Bump(ref value);
                return value;
            }

            // A value-type argument the cold arm takes the address of.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long ValueTypeArgument(Pair pair, bool cold)
            {
                if (cold)
                {
                    Twist(ref pair);
                    return pair.A * 3 + pair.B;
                }
                return pair.A - pair.B;
            }

            // A reference argument the cold arm replaces through its address, then collections.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ReplacedReference(Box box, bool cold)
            {
                if (cold)
                {
                    Swap(ref box);
                    Churn();
                    return box.Value;
                }
                return box.Value + 1;
            }

            // The hot arm holds a root across a collection, so the root frame stays at entry.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int HotArmRoot(Box box, bool cold)
            {
                if (cold)
                {
                    Swap(ref box);
                    Churn();
                    return box.Value;
                }
                var other = new Box(box.Value * 2);
                Churn();
                return other.Value + box.Value;
            }

            // The condition holds a root across a collection.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int ConditionRoot(Box box, bool cold)
            {
                var keep = new Box(box.Value + 9);
                Churn();
                if (cold)
                {
                    Swap(ref box);
                    return box.Value + keep.Value;
                }
                return keep.Value;
            }

            // The early arm is the hot one, so the framed arm continues into the code after the if.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long FramedTail(long value, bool hot)
            {
                if (hot) return value + 11;
                Bump(ref value);
                value ^= 0x1F;
                return value * 2;
            }

            public static int Run(int input)
            {
                long hash = 17;
                foreach (var cold in new[] { false, true })
                {
                    foreach (var value in new[]
                             {
                                 ColdArm(input, cold), WrittenBefore(input, cold), AddressedInCondition(input, cold),
                                 AddressedInBoth(input, cold), AddressedInOther(input, cold), AddressedBesideAFramedLocal(input, cold),
                                 ValueTypeArgument(new Pair { A = input, B = input * 3L + 1 }, cold),
                                 ReplacedReference(new Box(input), cold), HotArmRoot(new Box(input), cold),
                                 ConditionRoot(new Box(input), cold), FramedTail(input, !cold),
                             })
                    {
                        hash = hash * 31 + value;
                    }
                }
                return (int)(hash ^ (hash >> 32));
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases =
        [(0, 87585315), (1, 1191718230), (3, -1502193608), (7, -2122507580), (-4, -372382393)];

    [Theory]
    [InlineData(ManagedBranchPrefixDuplication.Auto)]
    [InlineData(ManagedBranchPrefixDuplication.Enabled)]
    [InlineData(ManagedBranchPrefixDuplication.Disabled)]
    public void EntersFramesOnTheArmThatNeedsThemOnlyWhileThatHolds(ManagedBranchPrefixDuplication duplication) =>
        LegalityCases.Run($"FrameShrinkWrappingLegality{duplication}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(BranchPrefixDuplication: duplication));
}
