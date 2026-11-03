using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// At the speed presets copies and fills of up to 128 bytes run inline rather
// than through memory.copy and memory.fill: every length, both overlap
// directions, every boundary between the chunk sizes, and the constant
// lengths the cascade folds to one case must still match desktop .NET.
public sealed class SmallBlockCopyCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void SmallCopiesAndFillsMatchDesktopAcrossLengthsAndOverlaps() =>
        runner.Run(new(
            "SmallBlockCopyCorpus",
            "NetWasm.Correctness.SmallBlockCopies",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            // The speed presets inline small blocks; Oz keeps memory.copy and memory.fill.
            Optimization = FinalWasmOptimization.O3,
        });

    internal const string Source = """
            using System;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.SmallBlockCopies;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    if (input == 7) return ConstantBlocks();
                    var hash = 17;
                    for (var length = 0; length <= 140; length++)
                    {
                        hash = unchecked(hash * 31 + input switch
                        {
                            0 => Overlapping(length, 0, 3),
                            1 => Overlapping(length, 5, 0),
                            2 => Overlapping(length, 1, 17),
                            3 => Block(length),
                            _ => Fill(length, (byte)(input * 37)),
                        });
                    }
                    return hash;
                }

                // Span.CopyTo and Array.Copy are memmove within one array.
                private static int Overlapping(int length, int from, int to)
                {
                    var data = Pattern(160);
                    data.AsSpan(from, length).CopyTo(data.AsSpan(to, length));
                    var array = Pattern(160);
                    Array.Copy(array, to, array, from, length);
                    return Mix(data) * 7 + Mix(array);
                }

                private static int Block(int length)
                {
                    var source = Pattern(150);
                    var destination = new byte[150];
                    Unsafe.CopyBlock(ref destination[3], ref source[1], (uint)length);
                    return Mix(destination);
                }

                private static int Fill(int length, byte value)
                {
                    var data = Pattern(150);
                    Unsafe.InitBlock(ref data[2], value, (uint)length);
                    return Mix(data);
                }

                // Lengths written as constants, around each case's bounds.
                private static int ConstantBlocks()
                {
                    var source = Pattern(150);
                    var hash = 17;
                    var destination = new byte[150];
                    Unsafe.CopyBlock(ref destination[3], ref source[1], 64);
                    hash = unchecked(hash * 31 + Mix(destination));
                    Unsafe.CopyBlock(ref destination[5], ref source[2], 65);
                    hash = unchecked(hash * 31 + Mix(destination));
                    Unsafe.CopyBlock(ref destination[7], ref source[3], 96);
                    hash = unchecked(hash * 31 + Mix(destination));
                    Unsafe.CopyBlock(ref destination[9], ref source[4], 127);
                    hash = unchecked(hash * 31 + Mix(destination));
                    Unsafe.CopyBlock(ref destination[11], ref source[5], 128);
                    hash = unchecked(hash * 31 + Mix(destination));
                    Unsafe.CopyBlock(ref destination[13], ref source[6], 129);
                    hash = unchecked(hash * 31 + Mix(destination));
                    Unsafe.InitBlock(ref destination[1], 0xA5, 65);
                    hash = unchecked(hash * 31 + Mix(destination));
                    Unsafe.InitBlock(ref destination[2], 0x3C, 128);
                    hash = unchecked(hash * 31 + Mix(destination));
                    Unsafe.InitBlock(ref destination[3], 0x5A, 129);
                    return unchecked(hash * 31 + Mix(destination));
                }

                private static byte[] Pattern(int length)
                {
                    var data = new byte[length];
                    for (var index = 0; index < length; index++) data[index] = (byte)(index * 7 + 1);
                    return data;
                }

                private static int Mix(byte[] data)
                {
                    var hash = 0;
                    foreach (var value in data) hash = unchecked(hash * 31 + value);
                    return hash;
                }

                public static int Trace() => 0;
            }
            """;
}
