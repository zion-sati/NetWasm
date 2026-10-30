namespace NetWasm.Compiler.Tests.Correctness;

// Span<T>, ReadOnlySpan<T>, Buffer, NativeMemory and the SpanHelpers they call are upstream's
// sources. The copies cover every size class of SpanHelpers.Memmove and the fill and clear
// helpers, with and without references and overlapping both ways; the exceptions compare types
// and parameter names, which upstream's ThrowHelper sets.
[Collection(CorrectnessTestGroup.Name)]
public sealed class SpanMemoryCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void SpanMemoryMatchesDesktopAcrossCompilerProfilesAndTargets() =>
        runner.Run(new(
            "SpanMemoryCorpus",
            "NetWasm.Correctness.SpanMemory",
            """
            using System;
            using System.Collections.Generic;
            using System.Runtime.InteropServices;

            namespace NetWasm.Correctness.SpanMemory;

            public struct Pair
            {
                public string? Name;
                public long Value;
            }

            public static class EntryPoint
            {
                private static readonly int[] Sizes =
                    [0, 1, 2, 3, 4, 5, 7, 8, 9, 15, 16, 17, 31, 32, 33, 48, 63, 64, 65, 100, 127, 128, 129,
                        255, 256, 257, 511, 512, 1000, 2047, 2048, 2049, 3000, 5000];

                public static int Run(int input) => input switch
                {
                    0 => CopyBytes(shift: 0),
                    1 => CopyBytes(shift: 3),
                    2 => CopyBytes(shift: -5),
                    3 => CopyValues(),
                    4 => CopyReferences(),
                    5 => FillValues(),
                    6 => ClearValues(),
                    7 => Native(),
                    8 => Blocks(),
                    9 => SpanExceptions(),
                    10 => BufferExceptions(),
                    11 => Text(),
                    _ => Searches(),
                };

                // Copies a prefix of a pattern onto itself, `shift` bytes later (or earlier).
                private static int CopyBytes(int shift)
                {
                    var hash = 17;
                    foreach (var size in Sizes)
                    {
                        var buffer = new byte[size + 16];
                        for (var index = 0; index < buffer.Length; index++)
                            buffer[index] = (byte)(index * 7 + 1);
                        var from = shift < 0 ? 8 : 4;
                        new ReadOnlySpan<byte>(buffer, from, size).CopyTo(new Span<byte>(buffer, from + shift, size));
                        hash = Mix(hash, buffer);
                    }

                    return hash;
                }

                private static int CopyValues()
                {
                    var hash = 23;
                    foreach (var size in Sizes)
                    {
                        var source = new long[size];
                        for (var index = 0; index < size; index++)
                            source[index] = index * 0x0101010101L - 3;
                        var destination = new long[size + 1];
                        var copied = source.AsSpan().TryCopyTo(destination.AsSpan(1));
                        var refused = source.AsSpan().TryCopyTo(destination.AsSpan(0, size == 0 ? 0 : size - 1));
                        hash = hash * 31 + (copied ? 1 : 0) + (refused ? 2 : 0);
                        foreach (var value in destination)
                            hash = unchecked(hash * 31 + value.GetHashCode());
                    }

                    return hash;
                }

                private static int CopyReferences()
                {
                    var hash = 29;
                    foreach (var size in Sizes)
                    {
                        if (size > 600)
                            continue;
                        var pairs = new Pair[size + 4];
                        for (var index = 0; index < pairs.Length; index++)
                            pairs[index] = new Pair { Name = "n" + index, Value = index };
                        pairs.AsSpan(0, size).CopyTo(pairs.AsSpan(3, size));
                        pairs.AsSpan(3, size).CopyTo(pairs.AsSpan(1, size));
                        GC.Collect();
                        foreach (var pair in pairs)
                            hash = unchecked(hash * 31 + (pair.Name?.Length ?? -1) * 1000 + (int)pair.Value);
                        hash = unchecked(hash * 31 + pairs[size].Name![^1]);
                    }

                    return hash;
                }

                private static int FillValues()
                {
                    var hash = 31;
                    foreach (var size in Sizes)
                    {
                        var bytes = new byte[size + 2];
                        bytes.AsSpan(1, size).Fill(0xA5);
                        var shorts = new short[size + 2];
                        shorts.AsSpan(1, size).Fill(-2);
                        var longs = new long[size + 2];
                        longs.AsSpan(1, size).Fill(long.MinValue + 5);
                        var pairs = new Pair[size + 2];
                        pairs.AsSpan(1, size).Fill(new Pair { Name = "x", Value = 9 });
                        hash = Mix(hash, bytes);
                        hash = Mix(hash, MemoryMarshal.AsBytes(shorts.AsSpan()).ToArray());
                        hash = Mix(hash, MemoryMarshal.AsBytes(longs.AsSpan()).ToArray());
                        foreach (var pair in pairs)
                            hash = unchecked(hash * 31 + (pair.Name?.Length ?? 0) + (int)pair.Value);
                    }

                    return hash;
                }

                private static int ClearValues()
                {
                    var hash = 37;
                    foreach (var size in Sizes)
                    {
                        var ints = new int[size + 2];
                        ints.AsSpan().Fill(-1);
                        ints.AsSpan(1, size).Clear();
                        var names = new string?[size + 2];
                        names.AsSpan().Fill("s");
                        names.AsSpan(1, size).Clear();
                        hash = Mix(hash, MemoryMarshal.AsBytes(ints.AsSpan()).ToArray());
                        foreach (var name in names)
                            hash = hash * 3 + (name is null ? 1 : 2);
                    }

                    return hash;
                }

                private static unsafe int Native()
                {
                    var hash = 41;
                    foreach (var size in Sizes)
                    {
                        var block = (byte*)NativeMemory.Alloc((nuint)size + 8);
                        NativeMemory.Fill(block, (nuint)size + 8, 0x3C);
                        NativeMemory.Clear(block + 4, (nuint)size);
                        NativeMemory.Copy(block, block + 1, (nuint)size);
                        var zeroed = (byte*)NativeMemory.AllocZeroed((nuint)size + 1, 1);
                        Buffer.MemoryCopy(block, zeroed, size + 1, size);
                        hash = Mix(hash, new ReadOnlySpan<byte>(block, size + 8).ToArray());
                        hash = Mix(hash, new ReadOnlySpan<byte>(zeroed, size + 1).ToArray());
                        NativeMemory.Free(block);
                        NativeMemory.Free(zeroed);
                    }

                    return hash;
                }

                private static int Blocks()
                {
                    var ints = new int[] { 1, 2, 3, 0x01020304 };
                    var doubles = new double[3];
                    Buffer.BlockCopy(ints, 4, doubles, 3, 12);
                    Buffer.BlockCopy(ints, 0, ints, 2, 8);
                    var hash = Mix(43, MemoryMarshal.AsBytes(doubles.AsSpan()).ToArray());
                    hash = Mix(hash, MemoryMarshal.AsBytes(ints.AsSpan()).ToArray());
                    Buffer.SetByte(ints, 13, 0x77);
                    return hash * 31 + Buffer.ByteLength(ints) * 1000 + Buffer.ByteLength(new char[5]) * 10 +
                        Buffer.GetByte(ints, 13) + Buffer.ByteLength(new nint[2]) / IntPtr.Size;
                }

                private static int SpanExceptions()
                {
                    var values = new int[4];
                    object[] covariant = new string[2];
                    var hash = 0;
                    hash = hash * 7 + Code(() => _ = new Span<int>(values, 3, 2));
                    hash = hash * 7 + Code(() => _ = new Span<int>(values, -1, 1));
                    hash = hash * 7 + Code(() => _ = new Span<int>(null, 1, 0));
                    hash = hash * 7 + Code(() => _ = new Span<int>(values).Slice(2, 3));
                    hash = hash * 7 + Code(() => _ = new ReadOnlySpan<int>(values).Slice(5));
                    hash = hash * 7 + Code(() => _ = new Span<int>(values)[4]);
                    hash = hash * 7 + Code(() => _ = new ReadOnlySpan<int>(values)[-1]);
                    hash = hash * 7 + Code(() => _ = new Span<object>(covariant));
                    hash = hash * 7 + Code(() => new Span<int>(values).CopyTo(new int[3]));
                    hash = hash * 7 + Code(() => new ReadOnlySpan<int>(values).CopyTo(new int[2]));
                    hash = hash * 7 + Code(() => _ = new ReadOnlySpan<object>(covariant).Length);
                    return hash;
                }

                private static int BufferExceptions()
                {
                    var hash = 0;
                    hash = hash * 7 + Code(() => Buffer.BlockCopy(new object[1], 0, new byte[4], 0, 1));
                    hash = hash * 7 + Code(() => Buffer.BlockCopy(new byte[4], 0, new string[1], 0, 1));
                    hash = hash * 7 + Code(() => Buffer.BlockCopy(null!, 0, new byte[4], 0, 1));
                    hash = hash * 7 + Code(() => Buffer.BlockCopy(new byte[4], -1, new byte[4], 0, 1));
                    hash = hash * 7 + Code(() => Buffer.BlockCopy(new byte[4], 2, new byte[4], 0, 3));
                    hash = hash * 7 + Code(() => Buffer.ByteLength(new Pair[1]));
                    hash = hash * 7 + Code(() => Buffer.GetByte(new short[2], 4));
                    hash = hash * 7 + Code(() => Buffer.SetByte(new int[1], -1, 0));
                    return hash;
                }

                private static int Text()
                {
                    Span<char> letters = "abc".ToCharArray();
                    ReadOnlySpan<char> digits = "0123";
                    var hash = (letters.ToString() == "abc" ? 1 : 0) + digits.Slice(1).ToString().Length * 1000;
                    hash += new Span<int>(new int[3]).ToString() == "System.Span<Int32>[3]" ? 10000 : 0;
                    hash += new ReadOnlySpan<byte>(new byte[2]).ToString() == "System.ReadOnlySpan<Byte>[2]" ? 20000 : 0;
            #pragma warning disable CS0618 // Equals and GetHashCode on spans are obsolete because they throw.
                    hash += Code(() => _ = new Span<int>(new int[1]).Equals(null)) * 100000;
                    hash += Code(() => _ = new ReadOnlySpan<int>(new int[1]).GetHashCode()) * 1000000;
            #pragma warning restore CS0618
                    return hash;
                }

                // span.BinarySearch(value) binds to upstream's overloads alone.
                private static int Searches()
                {
                    var values = new int[] { 1, 3, 5, 7, 9, 11 };
                    var span = new Span<int>(values);
                    ReadOnlySpan<int> readOnly = values;
                    var hash = span.BinarySearch(7) * 100 + span.BinarySearch(4);
                    hash = hash * 31 + readOnly.BinarySearch(11) * 10 + readOnly.BinarySearch(0);
                    hash = hash * 31 + span.BinarySearch(5, Comparer<int>.Default) + readOnly.BinarySearch(12, Comparer<int>.Default);
                    hash = hash * 31 + span.BinarySearch((IComparable<int>)3);
                    return hash;
                }

                private static int Code(Action action)
                {
                    try
                    {
                        action();
                        return 1;
                    }
                    catch (ArgumentNullException exception)
                    {
                        return 20 + Name(exception.ParamName);
                    }
                    catch (ArgumentOutOfRangeException exception)
                    {
                        return 40 + Name(exception.ParamName);
                    }
                    catch (ArgumentException exception)
                    {
                        return 60 + Name(exception.ParamName);
                    }
                    catch (IndexOutOfRangeException)
                    {
                        return 2;
                    }
                    catch (ArrayTypeMismatchException)
                    {
                        return 3;
                    }
                    catch (NotSupportedException)
                    {
                        return 4;
                    }
                }

                private static int Name(string? name) => name switch
                {
                    null => 0,
                    "src" => 1,
                    "dst" => 2,
                    "srcOffset" => 3,
                    "dstOffset" => 4,
                    "count" => 5,
                    "array" => 6,
                    "index" => 7,
                    "destination" => 8,
                    _ => 9,
                };

                private static int Mix(int hash, byte[] bytes)
                {
                    foreach (var value in bytes)
                        hash = unchecked(hash * 31 + value);
                    return unchecked(hash * 17 + bytes.Length);
                }

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12])
        {
            AllowUnsafe = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
        });
}
