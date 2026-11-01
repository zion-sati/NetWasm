namespace NetWasm.Compiler.Tests.Correctness;

// Buffer.Memmove copies up to 16 bytes with loads and stores and hands longer
// copies to memory.copy; Unsafe.CopyBlock and InitBlock use memory.copy and
// memory.fill. Every byte length from 0 to 40 crosses each size threshold, in
// both overlap directions and at every alignment.
public sealed class MemoryCopyCompatibilityTests(CorrectnessTestRunner runner)
    : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void CopiesAndFillsMatchDesktopAtEverySize() =>
        Run(new(
            "MemoryCopyCompatibility",
            "NetWasm.Correctness.MemoryCopyCompatibility",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11])
        {
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(
                CorpusMatrixProfile.Extended,
                Backend: CorpusExecutionBackend.Linked),
        });

    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace NetWasm.Correctness.MemoryCopyCompatibility;

        public sealed class Box
        {
            public Box(int value) => Value = value;

            public int Value { get; }
        }

        public struct Pair
        {
            public Box? First;
            public Box? Second;
        }

        public static class EntryPoint
        {
            private const int MaximumBytes = 40;

            public static int Run(int input) => input switch
            {
                0 => OverlappingBytes(forward: true),
                1 => OverlappingBytes(forward: false),
                2 => OverlappingChars(),
                3 => OverlappingIntegers(),
                4 => OverlappingLongs(),
                5 => EveryAlignment(),
                6 => OverlappingReferenceStructs(),
                7 => ShortStrings(),
                8 => BlockCopies(),
                9 => UnsafeCopyBlocks(),
                10 => UnsafeInitBlocks(),
                _ => ConstantSizeBlocks(),
            };

            public static int Trace() => 0;

            private static int OverlappingBytes(bool forward)
            {
                var hash = 17;
                for (var length = 0; length <= MaximumBytes; length++)
                {
                    for (var shift = 1; shift <= 9; shift += 4)
                    {
                        var values = Pattern(MaximumBytes + 16);
                        var source = forward ? shift : 0;
                        var destination = forward ? 0 : shift;
                        values.AsSpan(source, length).CopyTo(values.AsSpan(destination, length));
                        hash = Mix(hash, values);
                    }
                }
                return hash;
            }

            private static int OverlappingChars()
            {
                var hash = 17;
                for (var length = 0; length <= MaximumBytes / 2; length++)
                {
                    var forward = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();
                    forward.AsSpan(3, length).CopyTo(forward.AsSpan(0, length));
                    var backward = "abcdefghijklmnopqrstuvwxyz0123456789".ToCharArray();
                    backward.AsSpan(0, length).CopyTo(backward.AsSpan(3, length));
                    hash = Mix(Mix(hash, new string(forward)), new string(backward));
                }
                return hash;
            }

            private static int OverlappingIntegers()
            {
                var hash = 17;
                for (var length = 0; length <= MaximumBytes / 4; length++)
                {
                    var forward = new int[16];
                    var backward = new int[16];
                    for (var index = 0; index < 16; index++)
                    {
                        forward[index] = backward[index] = index * 1_000_003;
                    }
                    Array.Copy(forward, 1, forward, 0, length);
                    Array.Copy(backward, 0, backward, 1, length);
                    for (var index = 0; index < 16; index++)
                    {
                        hash = hash * 31 + forward[index];
                        hash = hash * 31 + backward[index];
                    }
                }
                return hash;
            }

            private static int OverlappingLongs()
            {
                var hash = 17;
                for (var length = 0; length <= MaximumBytes / 8; length++)
                {
                    var forward = new long[8];
                    var backward = new long[8];
                    for (var index = 0; index < 8; index++)
                    {
                        forward[index] = backward[index] = (index + 1) * 0x0102_0304_0506_0708L;
                    }
                    forward.AsSpan(1, length).CopyTo(forward);
                    backward.AsSpan(0, length).CopyTo(backward.AsSpan(1));
                    for (var index = 0; index < 8; index++)
                    {
                        hash = hash * 31 + forward[index].GetHashCode();
                        hash = hash * 31 + backward[index].GetHashCode();
                    }
                }
                return hash;
            }

            private static int EveryAlignment()
            {
                var hash = 17;
                var source = Pattern(64);
                for (var sourceOffset = 0; sourceOffset < 8; sourceOffset++)
                {
                    for (var destinationOffset = 0; destinationOffset < 8; destinationOffset++)
                    {
                        for (var length = 0; length <= 24; length++)
                        {
                            var destination = new byte[40];
                            source.AsSpan(sourceOffset, length).CopyTo(destination.AsSpan(destinationOffset));
                            hash = Mix(hash, destination);
                        }
                    }
                }
                return hash;
            }

            private static int OverlappingReferenceStructs()
            {
                var hash = 17;
                for (var length = 0; length <= 5; length++)
                {
                    var forward = Pairs(6);
                    var backward = Pairs(6);
                    forward.AsSpan(1, length).CopyTo(forward.AsSpan(0, length));
                    backward.AsSpan(0, length).CopyTo(backward.AsSpan(1, length));
                    GC.Collect();
                    hash = Mix(Mix(hash, forward), backward);
                }
                return hash;
            }

            private static int ShortStrings()
            {
                var hash = 17;
                var text = "0123456789abcdefghijklmnopqrstuvwxyz";
                for (var left = 0; left <= 18; left++)
                {
                    for (var right = 0; right <= 18; right += 3)
                    {
                        var joined = string.Concat(text.Substring(0, left), "-", text.Substring(right, 18 - right));
                        hash = Mix(hash, joined);
                        hash = Mix(hash, joined.Substring(left / 2, joined.Length / 2));
                    }
                    hash = Mix(hash, left.ToString() + left * 7919);
                }
                return hash;
            }

            private static int BlockCopies()
            {
                var hash = 17;
                for (var count = 0; count <= MaximumBytes; count++)
                {
                    var forward = new int[16];
                    var backward = new int[16];
                    for (var index = 0; index < 16; index++)
                    {
                        forward[index] = backward[index] = 0x1020_3040 + index;
                    }
                    Buffer.BlockCopy(forward, 3, forward, 0, count);
                    Buffer.BlockCopy(backward, 0, backward, 3, count);
                    for (var index = 0; index < 16; index++)
                    {
                        hash = hash * 31 + forward[index];
                        hash = hash * 31 + backward[index];
                    }
                }
                return hash;
            }

            private static int UnsafeCopyBlocks()
            {
                var hash = 17;
                var source = Pattern(MaximumBytes + 8);
                for (var length = 0; length <= MaximumBytes; length++)
                {
                    var aligned = new byte[MaximumBytes + 8];
                    Unsafe.CopyBlock(ref aligned[0], ref source[0], (uint)length);
                    var unaligned = new byte[MaximumBytes + 8];
                    Unsafe.CopyBlockUnaligned(ref unaligned[3], ref source[5], (uint)length);
                    hash = Mix(Mix(hash, aligned), unaligned);
                }
                return hash;
            }

            private static int UnsafeInitBlocks()
            {
                var hash = 17;
                for (var length = 0; length <= MaximumBytes; length++)
                {
                    var aligned = Pattern(MaximumBytes + 8);
                    Unsafe.InitBlock(ref aligned[0], 0xAB, (uint)length);
                    var unaligned = Pattern(MaximumBytes + 8);
                    Unsafe.InitBlockUnaligned(ref unaligned[3], 0x5C, (uint)length);
                    hash = Mix(Mix(hash, aligned), unaligned);
                }
                return hash;
            }

            private static int ConstantSizeBlocks()
            {
                var source = Pattern(32);
                var destination = new byte[32];
                Unsafe.CopyBlockUnaligned(ref destination[1], ref source[2], 1);
                Unsafe.CopyBlockUnaligned(ref destination[3], ref source[5], 2);
                Unsafe.CopyBlockUnaligned(ref destination[6], ref source[9], 4);
                Unsafe.CopyBlockUnaligned(ref destination[11], ref source[13], 8);
                Unsafe.CopyBlock(ref destination[16], ref source[1], 16);
                Unsafe.InitBlockUnaligned(ref destination[0], 0x7F, 1);
                Unsafe.InitBlock(ref destination[28], 0x33, 4);
                return Mix(17, destination);
            }

            private static byte[] Pattern(int length)
            {
                var values = new byte[length];
                for (var index = 0; index < length; index++)
                {
                    values[index] = (byte)(index * 37 + 11);
                }
                return values;
            }

            private static Pair[] Pairs(int length)
            {
                var values = new Pair[length];
                for (var index = 0; index < length; index++)
                {
                    values[index] = new Pair { First = new Box(index * 2), Second = new Box(index * 2 + 1) };
                }
                return values;
            }

            private static int Mix(int hash, byte[] values)
            {
                foreach (var value in values)
                {
                    hash = hash * 31 + value;
                }
                return hash;
            }

            private static int Mix(int hash, string value)
            {
                foreach (var character in value)
                {
                    hash = hash * 31 + character;
                }
                return hash * 31 + value.Length;
            }

            private static int Mix(int hash, Pair[] values)
            {
                foreach (var value in values)
                {
                    hash = hash * 31 + (value.First?.Value ?? -1);
                    hash = hash * 31 + (value.Second?.Value ?? -1);
                }
                return hash;
            }
        }
        """;
}
