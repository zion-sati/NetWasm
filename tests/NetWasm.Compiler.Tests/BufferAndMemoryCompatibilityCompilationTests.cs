// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class BufferAndMemoryCompatibilityCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesRawPrimitiveBufferSemantics(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            $"RawPrimitiveBuffer_{target}_{optimize}",
            RawPrimitiveBufferSource,
            "RawPrimitiveBufferFixture.EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(42, observed);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerValidatesBufferAndMemoryContractsForEveryProfile(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var observed = executor.Execute(new CompilationScenario(
            "BufferAndMemoryCompatibilityFixture",
            Source,
            "BufferAndMemoryCompatibilityFixture.EntryPoint",
            optimize,
            target,
            42,
            []));

        Assert.Equal(0, observed);
    }

    private const string Source = """
        using System;
        using System.Buffers;

        namespace BufferAndMemoryCompatibilityFixture;

        public enum Sample
        {
            FortyTwo = 42,
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                try
                {
                    if (Sample.FortyTwo.ToString("D") != "42") return 10;
                    var formatDestination = new char[4];
                    if (!((ISpanFormattable)Sample.FortyTwo).TryFormat(
                        formatDestination,
                        out var charsWritten,
                        "D",
                        null) ||
                        charsWritten != 2 ||
                        formatDestination[0] != '4' ||
                        formatDestination[1] != '2')
                    {
                        return 1;
                    }
                }
                catch
                {
                    return 11;
                }

                try
                {
                    var searchValues = SearchValues.Create(
                        new string[] { "--", "|" },
                        StringComparison.Ordinal);
                    if (!searchValues.Contains("--") || searchValues.Contains("-")) return 2;

                    ReadOnlySpan<char> source = " a--b| c ";
                    if (source.IndexOfAny(searchValues) != 2) return 3;
                    var ranges = new Range[4];
                    var count = source.SplitAny(
                        ranges,
                        new string[] { "--", "|" },
                        StringSplitOptions.TrimEntries);
                    if (count != 3 ||
                        !source[ranges[0]].SequenceEqual("a") ||
                        !source[ranges[1]].SequenceEqual("b") ||
                        !source[ranges[2]].SequenceEqual("c"))
                    {
                        return 4;
                    }
                }
                catch
                {
                    return 12;
                }

                try
                {
                    Memory<int> memory = new[] { input, 7 };
                    ReadOnlyMemory<int> readOnly = memory;
                    if (memory.IsEmpty || memory.Length != 2 ||
                        readOnly.IsEmpty || readOnly.Length != 2 ||
                        readOnly.Span[0] != input || Memory<int>.Empty.Length != 0)
                    {
                        return 5;
                    }
                }
                catch
                {
                    return 13;
                }

                try
                {
                    var pool = ArrayPool<int>.Create(64, 2);
                    var small = pool.Rent(1);
                    var medium = pool.Rent(17);
                    if (small.Length != 16 || medium.Length != 32 ||
                        pool.Rent(0).Length != 0)
                    {
                        return 6;
                    }
                    small[0] = 91;
                    medium[0] = 73;
                    pool.Return(small);
                    pool.Return(medium);
                    var sameSmall = pool.Rent(8);
                    var sameMedium = pool.Rent(24);
                    if (!ReferenceEquals(small, sameSmall) ||
                        !ReferenceEquals(medium, sameMedium))
                    {
                        return 7;
                    }
                    pool.Return(sameSmall, clearArray: true);
                    if (pool.Rent(16)[0] != 0)
                    {
                        return 8;
                    }
                    try
                    {
                        pool.Return(new int[17]);
                        return 9;
                    }
                    catch (ArgumentException)
                    {
                    }
                }
                catch
                {
                    return 14;
                }

                try
                {
                    if (!GenericTrimContracts()) return 15;
                }
                catch
                {
                    return 16;
                }

                return input - 42;
            }

            private static bool GenericTrimContracts()
            {
                var values = new[] { 0, 1, 2, 1, 0 };
                ReadOnlySpan<int> trimElements = new[] { 0, 1 };

                ReadOnlySpan<int> readOnlySpan = values;
                if (!readOnlySpan.Trim(0).SequenceEqual(new[] { 1, 2, 1 }) ||
                    !readOnlySpan.Trim(trimElements).SequenceEqual(new[] { 2 }) ||
                    !readOnlySpan.TrimStart(0).SequenceEqual(new[] { 1, 2, 1, 0 }) ||
                    !readOnlySpan.TrimEnd(0).SequenceEqual(new[] { 0, 1, 2, 1 }))
                {
                    return false;
                }

                var span = values.AsSpan();
                if (!span.Trim(0).SequenceEqual(new[] { 1, 2, 1 }) ||
                    !span.Trim(trimElements).SequenceEqual(new[] { 2 }))
                {
                    return false;
                }

                var memory = values.AsMemory();
                ReadOnlyMemory<int> readOnlyMemory = memory;
                if (!memory.Trim(0).Span.SequenceEqual(new[] { 1, 2, 1 }) ||
                    !memory.Trim(trimElements).Span.SequenceEqual(new[] { 2 }) ||
                    !readOnlyMemory.TrimStart(trimElements).Span.SequenceEqual(new[] { 2, 1, 0 }) ||
                    !readOnlyMemory.TrimEnd(trimElements).Span.SequenceEqual(new[] { 0, 1, 2 }))
                {
                    return false;
                }

                if (!readOnlySpan.Trim(ReadOnlySpan<int>.Empty).SequenceEqual(values))
                {
                    return false;
                }

                if (!GenericTrimDoesNotAllocate(values, trimElements))
                {
                    return false;
                }

                var nullableEdges = new string[] { null!, "middle", null! };
                var trimmedNulls = ((ReadOnlySpan<string>)nullableEdges).Trim((string)null!);
                return trimmedNulls.Length == 1 && trimmedNulls[0] == "middle";
            }

            private static bool GenericTrimDoesNotAllocate(
                int[] values,
                ReadOnlySpan<int> trimElements)
            {
                var span = values.AsSpan();
                ReadOnlySpan<int> readOnlySpan = values;
                var memory = values.AsMemory();
                ReadOnlyMemory<int> readOnlyMemory = memory;

                _ = span.Trim(0).Length;
                _ = readOnlySpan.Trim(trimElements).Length;
                _ = memory.Trim(0).Length;
                _ = readOnlyMemory.Trim(trimElements).Length;

                var before = GC.GetTotalAllocatedBytes();
                var checksum = 0;
                for (var index = 0; index < 100; index++)
                {
                    checksum += span.Trim(0).Length;
                    checksum += span.Trim(trimElements).Length;
                    checksum += readOnlySpan.Trim(0).Length;
                    checksum += readOnlySpan.Trim(trimElements).Length;
                    checksum += memory.Trim(0).Length;
                    checksum += memory.Trim(trimElements).Length;
                    checksum += readOnlyMemory.Trim(0).Length;
                    checksum += readOnlyMemory.Trim(trimElements).Length;
                }

                return GC.GetTotalAllocatedBytes() - before == 0 && checksum == 1_600;
            }
        }
        """;

    private const string RawPrimitiveBufferSource = """
        using System;

        namespace RawPrimitiveBufferFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (!PrimitiveWidthsAreComplete()) return -1;
                if (!FloatingPointBytesRoundTrip()) return -2;
                if (!BooleanBytesRemainNoncanonical()) return -3;
                if (!BlockCopyPreservesRawBytesAndOverlap()) return -4;
                if (!ExceptionOrderMatchesTheRuntimeContract()) return -5;
                if (!SetBytePreservesNeighboringBytes()) return -6;
                return input + 1;
            }

            private static bool PrimitiveWidthsAreComplete()
            {
                if (Buffer.ByteLength(new byte[1]) != 1 ||
                    Buffer.ByteLength(new sbyte[1]) != 1 ||
                    Buffer.ByteLength(new bool[1]) != 1 ||
                    Buffer.ByteLength(new char[1]) != 2 ||
                    Buffer.ByteLength(new short[1]) != 2 ||
                    Buffer.ByteLength(new ushort[1]) != 2 ||
                    Buffer.ByteLength(new int[1]) != 4 ||
                    Buffer.ByteLength(new uint[1]) != 4 ||
                    Buffer.ByteLength(new float[1]) != 4 ||
                    Buffer.ByteLength(new long[1]) != 8 ||
                    Buffer.ByteLength(new ulong[1]) != 8 ||
                    Buffer.ByteLength(new double[1]) != 8 ||
                    Buffer.ByteLength(new IntPtr[1]) != IntPtr.Size ||
                    Buffer.ByteLength(new UIntPtr[1]) != IntPtr.Size)
                {
                    return false;
                }

                var signed = new IntPtr[1];
                var unsigned = new UIntPtr[1];
                Buffer.SetByte(signed, IntPtr.Size - 1, 0x5a);
                Buffer.SetByte(unsigned, IntPtr.Size - 1, 0xa5);
                return Buffer.GetByte(signed, IntPtr.Size - 1) == 0x5a &&
                    Buffer.GetByte(unsigned, IntPtr.Size - 1) == 0xa5;
            }

            private static bool FloatingPointBytesRoundTrip()
            {
                var singles = new[] { 1.0f };
                if (Buffer.GetByte(singles, 0) != 0 ||
                    Buffer.GetByte(singles, 1) != 0 ||
                    Buffer.GetByte(singles, 2) != 0x80 ||
                    Buffer.GetByte(singles, 3) != 0x3f)
                {
                    return false;
                }
                Buffer.SetByte(singles, 2, 0);
                Buffer.SetByte(singles, 3, 0x40);
                if (Buffer.GetByte(singles, 0) != 0 ||
                    Buffer.GetByte(singles, 1) != 0 ||
                    Buffer.GetByte(singles, 2) != 0 ||
                    Buffer.GetByte(singles, 3) != 0x40 || singles[0] != 2.0f)
                {
                    return false;
                }

                var doubles = new[] { 1.0 };
                if (Buffer.GetByte(doubles, 6) != 0xf0 ||
                    Buffer.GetByte(doubles, 7) != 0x3f)
                {
                    return false;
                }
                Buffer.SetByte(doubles, 6, 0);
                Buffer.SetByte(doubles, 7, 0x40);
                return Buffer.GetByte(doubles, 6) == 0 &&
                    Buffer.GetByte(doubles, 7) == 0x40 && doubles[0] == 2.0;
            }

            private static bool BooleanBytesRemainNoncanonical()
            {
                var values = new[] { false };
                Buffer.SetByte(values, 0, 0xa5);
                var copy = new byte[1];
                Buffer.BlockCopy(values, 0, copy, 0, 1);
                return values[0] && Buffer.GetByte(values, 0) == 0xa5 &&
                    copy[0] == 0xa5;
            }

            private static bool SetBytePreservesNeighboringBytes()
            {
                var values = new[] { 0x11223344, 0x55667788 };
                Buffer.SetByte(values, 1, 0xaa);
                Buffer.SetByte(values, 7, 0xbb);
                return Buffer.GetByte(values, 0) == 0x44 &&
                    Buffer.GetByte(values, 1) == 0xaa &&
                    Buffer.GetByte(values, 2) == 0x22 &&
                    Buffer.GetByte(values, 3) == 0x11 &&
                    Buffer.GetByte(values, 4) == 0x88 &&
                    Buffer.GetByte(values, 5) == 0x77 &&
                    Buffer.GetByte(values, 6) == 0x66 &&
                    Buffer.GetByte(values, 7) == 0xbb;
            }

            private static bool BlockCopyPreservesRawBytesAndOverlap()
            {
                var source = new[] { 1.0f };
                var bytes = new byte[4];
                Buffer.BlockCopy(source, 0, bytes, 0, bytes.Length);
                if (bytes[0] != 0 || bytes[1] != 0 ||
                    bytes[2] != 0x80 || bytes[3] != 0x3f)
                {
                    return false;
                }

                bytes[2] = 0;
                bytes[3] = 0x40;
                Buffer.BlockCopy(bytes, 0, source, 0, bytes.Length);
                if (source[0] != 2.0f) return false;

                var forwards = new byte[] { 1, 2, 3, 4, 5 };
                Buffer.BlockCopy(forwards, 0, forwards, 1, 4);
                if (forwards[0] != 1 || forwards[1] != 1 ||
                    forwards[2] != 2 || forwards[3] != 3 || forwards[4] != 4)
                {
                    return false;
                }

                var backwards = new byte[] { 1, 2, 3, 4, 5 };
                Buffer.BlockCopy(backwards, 1, backwards, 0, 4);
                if (backwards[0] != 2 || backwards[1] != 3 ||
                    backwards[2] != 4 || backwards[3] != 5 || backwards[4] != 5)
                {
                    return false;
                }

                var unalignedSource = new byte[] { 9, 0x44, 0x33, 8 };
                var unalignedDestination = new int[] { 0x11223344 };
                Buffer.BlockCopy(unalignedSource, 1, unalignedDestination, 1, 2);
                if (Buffer.GetByte(unalignedDestination, 0) != 0x44 ||
                    Buffer.GetByte(unalignedDestination, 1) != 0x44 ||
                    Buffer.GetByte(unalignedDestination, 2) != 0x33 ||
                    Buffer.GetByte(unalignedDestination, 3) != 0x11)
                {
                    return false;
                }

                var disjoint = new byte[] { 1, 2, 3, 4 };
                Buffer.BlockCopy(disjoint, 0, disjoint, 2, 2);
                if (disjoint[0] != 1 || disjoint[1] != 2 ||
                    disjoint[2] != 1 || disjoint[3] != 2)
                {
                    return false;
                }
                Buffer.BlockCopy(disjoint, 0, disjoint, 0, disjoint.Length);
                Buffer.BlockCopy(disjoint, disjoint.Length, disjoint,
                    disjoint.Length, 0);
                Buffer.BlockCopy(Array.Empty<byte>(), 0, Array.Empty<byte>(), 0, 0);
                return disjoint[0] == 1 && disjoint[3] == 2;
            }

            private static bool ExceptionOrderMatchesTheRuntimeContract() =>
                RejectNullSource() && RejectNullDestination() &&
                RejectNonprimitiveSourceBeforeOffset() &&
                RejectNonprimitiveDestinationBeforeOffset() &&
                RejectNegativePrimitiveOffset() && RejectNegativeCount() &&
                RejectInvalidRangeWithoutMutation() &&
                RejectNonprimitiveByteAccess() && RejectPrimitiveByteRange();

            private static bool RejectNullSource()
            {
                try
                {
                    Buffer.BlockCopy(null!, 0, new byte[1], 0, 1);
                    return false;
                }
                catch (ArgumentNullException)
                {
                    return true;
                }
            }

            private static bool RejectNullDestination()
            {
                try
                {
                    Buffer.BlockCopy(new byte[1], 0, null!, 0, 1);
                    return false;
                }
                catch (ArgumentNullException)
                {
                    return true;
                }
            }

            private static bool RejectNonprimitiveSourceBeforeOffset()
            {
                try
                {
                    Buffer.BlockCopy(new object[1], -1, new byte[1], 0, 1);
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            }

            private static bool RejectNonprimitiveDestinationBeforeOffset()
            {
                try
                {
                    Buffer.BlockCopy(new byte[1], 0, new object[1], -1, 1);
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            }

            private static bool RejectNegativePrimitiveOffset()
            {
                try
                {
                    Buffer.BlockCopy(new byte[1], -1, new byte[1], 0, 1);
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool RejectNegativeCount()
            {
                try
                {
                    Buffer.BlockCopy(new byte[1], 0, new byte[1], 0, -1);
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return true;
                }
            }

            private static bool RejectInvalidRangeWithoutMutation()
            {
                var destination = new byte[] { 91 };
                try
                {
                    Buffer.BlockCopy(new byte[1], 0, destination, 0, 2);
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
                catch (ArgumentException)
                {
                    return destination[0] == 91;
                }
            }

            private static bool RejectNonprimitiveByteAccess()
            {
                try
                {
                    _ = Buffer.ByteLength(new decimal[1]);
                    return false;
                }
                catch (ArgumentException)
                {
                }

                try
                {
                    _ = Buffer.GetByte(new object[1], -1);
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
                catch (ArgumentException)
                {
                }

                try
                {
                    Buffer.SetByte(new object[1], -1, 1);
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
                catch (ArgumentException)
                {
                    return true;
                }
            }

            private static bool RejectPrimitiveByteRange()
            {
                try
                {
                    _ = Buffer.GetByte(new byte[1], -1);
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                }

                var destination = new byte[] { 73 };
                try
                {
                    Buffer.SetByte(destination, 1, 1);
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return destination[0] == 73;
                }
            }
        }
        """;
}
