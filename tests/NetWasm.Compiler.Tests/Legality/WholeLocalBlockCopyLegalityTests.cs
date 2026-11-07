using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.Tests.Legality;

// A block copy of a whole local into another becomes the copy of the local,
// and its destination reads the source only while neither changes: each method
// breaks one condition, and each gives desktop .NET 11's result: the
// destination read by value, which reads the source, the source given a new
// value before that read, the source changed after the copy, the destination
// read in another block, half the
// struct copied, the destination's address escaping, a loop copying a source
// it changes, a loop repeating one copy, a struct without fields that declares
// its size, the ref form through Unsafe.As, unaligned, the source changed through
// its address after the copy, and a copy between two types of one size.
public sealed class WholeLocalBlockCopyLegalityTests
{
    private const string Source = """

        using System;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        public struct Big
        {
            public long A, B, C, D;
        }

        public struct Twin
        {
            public long X, Y;
        }

        public struct Pair
        {
            public double P, Q;
        }

        [StructLayout(LayoutKind.Explicit, Size = 64)]
        public struct Raw
        {
        }

        public static unsafe class EntryPoint
        {
            private static Big Make(int input) => new Big { A = input, B = input * 3L, C = -input, D = input ^ 0x55 };

            private static long Hash(in Big value) => value.A * 31 + value.B * 17 + value.C * 7 + value.D;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Value(Big value) => value.A * 31 + value.B * 17 + value.C * 7 + value.D;

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Clobber(ref Big value) => value.A = -7777;

            // The source's address goes to a call that changes it after the copy: the destination keeps the copy.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long SourceByAddress(int input)
            {
                var source = Make(input);
                Big destination;
                Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Big));
                Clobber(ref source);
                return Value(destination) * 3 + source.A;
            }

            // A copy between two types of one size moves bytes, not a value of either type.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long OtherType(int input)
            {
                var source = new Twin { X = input * 1000003L, Y = -input };
                Pair destination;
                Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Pair));
                return BitConverter.DoubleToInt64Bits(destination.P) ^ BitConverter.DoubleToInt64Bits(destination.Q) * 3;
            }

            // The destination read by value after the copy: it reads the source.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Propagated(int input)
            {
                var source = Make(input);
                Big destination;
                Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Big));
                return Value(destination);
            }

            // The source is given a new value after the copy, before the destination is read.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long SourceReassigned(int input)
            {
                var source = Make(input);
                Big destination;
                Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Big));
                source = Make(input + 1);
                return Value(destination) * 3 + Value(source);
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static void Touch(ref Big value) => value.C += 1000;

            // A whole copy, its destination read later in the block.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Whole(int input)
            {
                var source = Make(input);
                Big destination;
                Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Big));
                return Hash(destination);
            }

            // The source changes after the copy: the destination keeps the copied value.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long SourceChanged(int input)
            {
                var source = Make(input);
                Big destination;
                Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Big));
                source.A = 99;
                source = Make(input + 1);
                return Hash(destination) * 3 + Hash(source);
            }

            // The destination is read on a branch, in another block.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long OtherBlock(int input)
            {
                var source = Make(input);
                Big destination;
                Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Big));
                if (input > 2) return Hash(destination);
                return -Hash(destination);
            }

            // A copy of half the struct leaves the rest of the destination as it was.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Partial(int input)
            {
                var source = Make(input);
                var destination = Make(-input - 5);
                Unsafe.CopyBlock(&destination, &source, 16);
                return Hash(destination);
            }

            // The destination's address escapes after the copy.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Escapes(int input)
            {
                var source = Make(input);
                Big destination;
                Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Big));
                Touch(ref destination);
                return Hash(destination);
            }

            // A loop copies a source it changes, so every iteration differs.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Changing(int input)
            {
                var source = Make(input);
                Big destination = default;
                long total = 0;
                for (var i = 0; i < 5; i++)
                {
                    source.A = i * input;
                    Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Big));
                    total += Hash(destination);
                }
                return total;
            }

            // A loop repeats one copy, then the destination is read.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Repeated(int input)
            {
                var source = Make(input);
                Big destination = default;
                for (var i = 0; i < 100; i++)
                {
                    Unsafe.CopyBlock(&destination, &source, (uint)sizeof(Big));
                }
                return Hash(destination);
            }

            // A struct without fields that declares its size, copied by a constant.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Declared(int input)
            {
                Raw source;
                Raw destination;
                var bytes = (byte*)&source;
                for (var i = 0; i < 64; i++) bytes[i] = (byte)(i * input + 1);
                Unsafe.CopyBlock(&destination, &source, 64);
                long total = 0;
                var copied = (byte*)&destination;
                for (var i = 0; i < 64; i++) total = total * 31 + copied[i];
                return total;
            }

            // The ref form of upstream code, through Unsafe.As and SizeOf, unaligned.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static long Viewed(int input)
            {
                var source = Make(input);
                Big destination = default;
                Unsafe.CopyBlockUnaligned(ref Unsafe.As<Big, byte>(ref destination), ref Unsafe.As<Big, byte>(ref source),
                    (uint)Unsafe.SizeOf<Big>());
                return Hash(destination);
            }

            public static int Run(int input)
            {
                long hash = 17;
                foreach (var value in new[] { Whole(input), Propagated(input), SourceReassigned(input), SourceChanged(input), OtherBlock(input), Partial(input), Escapes(input),
                             Changing(input), Repeated(input), Declared(input), Viewed(input), SourceByAddress(input), OtherType(input) })
                {
                    hash = hash * 31 + value;
                }
                return (int)(hash ^ (hash >> 32));
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases =
        [(0, -1341803044), (1, -1653717233), (3, -112925535), (7, -832242584), (-4, 1400012887)];

    [Theory]
    [InlineData(ManagedAddressTakenValuePromotion.Auto)]
    [InlineData(ManagedAddressTakenValuePromotion.Enabled)]
    [InlineData(ManagedAddressTakenValuePromotion.Disabled)]
    public void CopiesAWholeLocalAsTheLocalOnlyWhileTheCopyHolds(ManagedAddressTakenValuePromotion promotion) =>
        LegalityCases.Run($"WholeLocalBlockCopyLegality{promotion}", Source, Cases, FinalWasmOptimization.O3,
            new WasmOptimizationOverrides(AddressTakenValuePromotion: promotion), allowUnsafe: true);
}
