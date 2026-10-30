using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// At the speed presets copies and fills of up to 64 bytes run inline rather
// than through memory.copy and memory.fill: every length, both overlap
// directions, and every boundary between the chunk sizes must still match
// desktop .NET.
[Collection(CorrectnessTestGroup.Name)]
public sealed class SmallBlockCopyCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void SmallCopiesAndFillsMatchDesktopAcrossLengthsAndOverlaps() =>
        runner.Run(new(
            "SmallBlockCopyCorpus",
            "NetWasm.Correctness.SmallBlockCopies",
            """
            using System;
            using System.Runtime.CompilerServices;

            namespace NetWasm.Correctness.SmallBlockCopies;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var hash = 17;
                    for (var length = 0; length <= 70; length++)
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
                    var data = Pattern(100);
                    data.AsSpan(from, length).CopyTo(data.AsSpan(to, length));
                    var array = Pattern(100);
                    Array.Copy(array, to, array, from, length);
                    return Mix(data) * 7 + Mix(array);
                }

                private static int Block(int length)
                {
                    var source = Pattern(80);
                    var destination = new byte[80];
                    Unsafe.CopyBlock(ref destination[3], ref source[1], (uint)length);
                    return Mix(destination);
                }

                private static int Fill(int length, byte value)
                {
                    var data = Pattern(80);
                    Unsafe.InitBlock(ref data[2], value, (uint)length);
                    return Mix(data);
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
            """,
            [0, 1, 2, 3, 4, 5, 6])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            // The speed presets inline small blocks; Oz keeps memory.copy and memory.fill.
            Optimization = FinalWasmOptimization.O3,
        });
}
