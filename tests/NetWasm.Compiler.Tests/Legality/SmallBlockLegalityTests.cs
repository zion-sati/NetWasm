using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Legality;

// Blocks up to 128 bytes are copied and filled inline at the speed presets,
// a few vector stores whose chunks overlap at the end, and a copy loads every
// chunk before it stores any. Overlapping copies at every size from 1 to
// 140, the destination ahead of the source and behind it by 1, 7, 16, 33 and
// 64 bytes, fills at every size, and Unsafe.CopyBlock and InitBlock of
// constant sizes around each case's bounds must leave the guard bytes around
// them, and the bytes between, as desktop .NET does.
public sealed class SmallBlockLegalityTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        public static class EntryPoint
        {
            private const int Guard = 24;

            private static int Hash(byte[] values)
            {
                var hash = 17;
                foreach (var value in values) hash = unchecked(hash * 31 + value);
                return hash;
            }

            private static byte[] Buffer(int size)
            {
                var buffer = new byte[size + 2 * Guard + 64];
                for (var i = 0; i < buffer.Length; i++) buffer[i] = (byte)(i * 7 + 3);
                return buffer;
            }

            // Overlapping copies at every size, the destination ahead of and behind the source.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Overlapping(int shift)
            {
                var hash = 0;
                for (var size = 1; size <= 140; size++)
                {
                    var forward = Buffer(size);
                    forward.AsSpan(Guard, size).CopyTo(forward.AsSpan(Guard + shift, size));
                    var backward = Buffer(size);
                    backward.AsSpan(Guard + shift, size).CopyTo(backward.AsSpan(Guard, size));
                    hash = unchecked(hash * 31 + Hash(forward) * 7 + Hash(backward));
                }
                return hash;
            }

            // Fills at every size, between guard bytes.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Fills(byte value)
            {
                var hash = 0;
                for (var size = 1; size <= 140; size++)
                {
                    var buffer = Buffer(size);
                    buffer.AsSpan(Guard, size).Fill(value);
                    hash = unchecked(hash * 31 + Hash(buffer));
                }
                return hash;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int Constant(int size, Action<byte[]> copy)
            {
                var buffer = Buffer(size);
                copy(buffer);
                return Hash(buffer);
            }

            // Unsafe.CopyBlock and InitBlock of constant sizes around each case's bounds.
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static unsafe int ConstantBlocks(byte value)
            {
                var hash = 0;
                foreach (var size in new[] { 1, 2, 3, 4, 7, 8, 15, 16, 17, 31, 32, 33, 63, 64, 65, 127, 128, 129 })
                {
                    hash = unchecked(hash * 31 + Constant(size, buffer =>
                    {
                        fixed (byte* start = buffer)
                        {
                            switch (size)
                            {
                                case 1: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 1); Unsafe.InitBlock(start + Guard + 40, value, 1); break;
                                case 2: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 2); Unsafe.InitBlock(start + Guard + 40, value, 2); break;
                                case 3: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 3); Unsafe.InitBlock(start + Guard + 40, value, 3); break;
                                case 4: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 4); Unsafe.InitBlock(start + Guard + 40, value, 4); break;
                                case 7: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 7); Unsafe.InitBlock(start + Guard + 40, value, 7); break;
                                case 8: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 8); Unsafe.InitBlock(start + Guard + 40, value, 8); break;
                                case 15: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 15); Unsafe.InitBlock(start + Guard + 40, value, 15); break;
                                case 16: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 16); Unsafe.InitBlock(start + Guard + 40, value, 16); break;
                                case 17: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 17); Unsafe.InitBlock(start + Guard + 40, value, 17); break;
                                case 31: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 31); Unsafe.InitBlock(start + Guard + 40, value, 31); break;
                                case 32: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 32); Unsafe.InitBlock(start + Guard + 40, value, 32); break;
                                case 33: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 33); Unsafe.InitBlock(start + Guard + 40, value, 33); break;
                                case 63: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 63); Unsafe.InitBlock(start + Guard + 40, value, 63); break;
                                case 64: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 64); Unsafe.InitBlock(start + Guard + 40, value, 64); break;
                                case 65: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 65); Unsafe.InitBlock(start + Guard + 40, value, 65); break;
                                case 127: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 127); Unsafe.InitBlock(start + Guard + 40, value, 127); break;
                                case 128: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 128); Unsafe.InitBlock(start + Guard + 40, value, 128); break;
                                default: Unsafe.CopyBlock(start + Guard, start + Guard + 70, 129); Unsafe.InitBlock(start + Guard + 40, value, 129); break;
                            }
                        }
                    }));
                }
                return hash;
            }

            public static int Run(int input)
            {
                var which = input / 100; input %= 100;
                return which switch
                {
                    0 => Overlapping(input),
                    1 => Fills((byte)(input * 37 + 1)),
                    2 => ConstantBlocks((byte)(input * 37 + 1)),
                    _ => -9,
                };
            }
        }

        """;

    // As desktop .NET 11 computes them.
    private static readonly (int Input, int Expected)[] Cases = [(1, -2075663088), (7, 2099744048), (16, 435389664), (33, 1126994704), (64, 567372832), (100, -460063498), (101, -2035245608), (200, 475135141), (201, -10716746)];

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O0)]
    [InlineData(FinalWasmOptimization.O1)]
    [InlineData(FinalWasmOptimization.O3)]
    [InlineData(FinalWasmOptimization.Oz)]
    public void CopiesAndFillsOnlyTheirBytes(FinalWasmOptimization optimization) =>
        LegalityCases.Run($"SmallBlockLegality{optimization}", Source, Cases, optimization, allowUnsafe: true);
}
