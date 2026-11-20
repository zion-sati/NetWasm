// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// MemoryMarshal behaves as desktop .NET's: Cast truncates a length that does not fill the last element and checks
// only the resulting length for overflow, AsBytes checks the byte length, Read/Write/AsRef reject short spans and
// types with references, ToEnumerable returns a whole array or string itself, and GetArrayDataReference gives a
// valid reference for an empty array and the first element of a rectangular one. The expected values are desktop
// .NET's own, computed here. ArrayElementTypeCompilationTests covers Buffer and Array.Copy over element types.
public sealed class MemoryMarshalCompilationTests
{
    // The same layouts as the program's structs, so desktop computes the same sizes.
    [StructLayout(LayoutKind.Sequential, Size = 3)]
    private struct B3
    {
        private readonly byte _a;
    }

    [StructLayout(LayoutKind.Sequential, Size = 5)]
    private struct B5
    {
        private readonly byte _a;
    }

    [StructLayout(LayoutKind.Sequential, Size = 12)]
    private struct V12
    {
        private readonly int _x;
    }

    private static readonly string[] TypeNames = ["byte", "short", "B3", "int", "B5", "long", "V12"];

    private static readonly int[] Lengths = [0, 1, 2, 3, 5, 7, 13];

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void BehavesAsDesktop(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            "MemoryMarshalBehavior", Source(), "EntryPoint", optimized, target, 0, [])
        { AllowUnsafe = true }));
    }

    private static string Source()
    {
        var checks = new StringBuilder();
        var check = 100;
        // A check that throws reports its number plus 10000.
        void Expect(string condition)
        {
            check++;
            checks.AppendLine(CultureInfo.InvariantCulture,
                $"        try {{ if (!({condition})) return {check}; }} catch (Exception) {{ return {check + 10000}; }}");
        }
        void Throws<TException>(string statement) where TException : Exception =>
            checks.AppendLine(CultureInfo.InvariantCulture,
                $"        try {{ {statement}; return {++check}; }} catch ({typeof(TException).Name}) {{ }}");

        // Cast between every pair of element sizes, over lengths that do and do not fill the last target element,
        // for Span and ReadOnlySpan: the resulting length and a checksum of the bytes it covers.
        foreach (var from in TypeNames)
        {
            foreach (var to in TypeNames)
            {
                foreach (var length in Lengths)
                {
                    var (castLength, sum) = CastOnDesktop(from, to, length);
                    Expect($"CastLength<{from}, {to}>({length}) == {castLength} && CastSum<{from}, {to}>({length}) == {sum}L");
                    Expect($"ReadOnlyCastLength<{from}, {to}>({length}) == {castLength}");
                }
            }
        }

        // Lengths far beyond the backing storage: only the arithmetic is observed.
        long anchor = 0;
        byte byteAnchor = 0;
        Expect($"MemoryMarshal.Cast<long, int>(MemoryMarshal.CreateSpan(ref anchor, 300_000_000)).Length == " +
            MemoryMarshal.Cast<long, int>(MemoryMarshal.CreateSpan(ref anchor, 300_000_000)).Length);
        Expect($"MemoryMarshal.Cast<byte, int>(MemoryMarshal.CreateSpan(ref byteAnchor, int.MaxValue)).Length == " +
            MemoryMarshal.Cast<byte, int>(MemoryMarshal.CreateSpan(ref byteAnchor, int.MaxValue)).Length);
        Expect($"MemoryMarshal.Cast<byte, B3>(MemoryMarshal.CreateReadOnlySpan(ref byteAnchor, int.MaxValue)).Length == " +
            MemoryMarshal.Cast<byte, B3>(MemoryMarshal.CreateReadOnlySpan(ref byteAnchor, int.MaxValue)).Length);
        Expect($"MemoryMarshal.Cast<B5, V12>(MemoryMarshal.CreateSpan(ref Unsafe.As<long, B5>(ref anchor), 400_000_000)).Length == " +
            MemoryMarshal.Cast<B5, V12>(MemoryMarshal.CreateSpan(ref Unsafe.As<long, B5>(ref anchor), 400_000_000)).Length);
        Throws<OverflowException>("MemoryMarshal.Cast<long, byte>(MemoryMarshal.CreateSpan(ref anchor, 300_000_000))");
        Throws<OverflowException>("MemoryMarshal.Cast<int, short>(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<long, int>(ref anchor), 1_500_000_000))");
        Throws<OverflowException>("MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref anchor, 300_000_000))");
        Throws<OverflowException>("MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<long, V12>(ref anchor), 200_000_000))");
        Expect($"MemoryMarshal.CreateSpan(ref anchor, -1).Length == {MemoryMarshal.CreateSpan(ref anchor, -1).Length}");

        // Types with references are rejected by every reinterpreting member.
        Throws<ArgumentException>("MemoryMarshal.Cast<byte, Holder>(new byte[8].AsSpan())");
        Throws<ArgumentException>("MemoryMarshal.Cast<Holder, byte>(new Holder[1].AsSpan())");
        Throws<ArgumentException>("MemoryMarshal.Cast<byte, Holder>((ReadOnlySpan<byte>)new byte[8])");
        Throws<ArgumentException>("MemoryMarshal.AsBytes(new Holder[1].AsSpan())");
        Throws<ArgumentException>("MemoryMarshal.Read<Holder>(new byte[16])");
        Throws<ArgumentException>("MemoryMarshal.TryRead<Holder>(new byte[16], out _)");
        Throws<ArgumentException>("MemoryMarshal.Write(new byte[16].AsSpan(), new Holder())");
        Throws<ArgumentException>("MemoryMarshal.TryWrite(new byte[16].AsSpan(), new Holder())");
        Throws<ArgumentException>("MemoryMarshal.AsRef<Holder>(new byte[16].AsSpan())");

        // Read, Write and AsRef at unaligned offsets, and their short-span behavior.
        var buffer = new byte[16];
        for (var i = 0; i < buffer.Length; i++) buffer[i] = (byte)(i * 37 + 11);
        Expect("Fill() is { } b && MemoryMarshal.Read<B5>(b.AsSpan(3)) is var r5 && " +
            $"r5.A == {MemoryMarshal.Read<byte>(MemoryMarshal.AsBytes([MemoryMarshal.Read<B5>(buffer.AsSpan(3))]))} && " +
            $"r5.E == {MemoryMarshal.AsBytes([MemoryMarshal.Read<B5>(buffer.AsSpan(3))])[4]}");
        Expect("Fill() is { } b2 && MemoryMarshal.Read<V12>(b2.AsSpan(1)) is var r12 && " +
            $"r12.X == {MemoryMarshal.Cast<V12, int>([MemoryMarshal.Read<V12>(buffer.AsSpan(1))])[0]} && " +
            $"r12.Z == {MemoryMarshal.Cast<V12, int>([MemoryMarshal.Read<V12>(buffer.AsSpan(1))])[2]}");
        var written = new byte[8];
        MemoryMarshal.Write(written.AsSpan(1), MemoryMarshal.Read<B3>([1, 2, 3]));
        MemoryMarshal.Write(written.AsSpan(4), 0x0A0B0C);
        Expect($"Written() == {BitConverter.ToInt64(written)}L");
        Expect($"MemoryMarshal.TryRead<long>(new byte[7], out var shortRead) == {MemoryMarshal.TryRead<long>(new byte[7], out _).ToString().ToLowerInvariant()} && shortRead == 0");
        Expect($"MemoryMarshal.TryWrite(new byte[2].AsSpan(), new B3()) == {MemoryMarshal.TryWrite(new byte[2].AsSpan(), new B3()).ToString().ToLowerInvariant()}");
        Expect($"MemoryMarshal.TryWrite(new byte[3].AsSpan(), new B3()) == {MemoryMarshal.TryWrite(new byte[3].AsSpan(), new B3()).ToString().ToLowerInvariant()}");
        Throws<ArgumentOutOfRangeException>("MemoryMarshal.Read<V12>(new byte[11])");
        Throws<ArgumentOutOfRangeException>("MemoryMarshal.Write(new byte[4].AsSpan(), new B5())");
        Throws<ArgumentOutOfRangeException>("MemoryMarshal.AsRef<long>(new byte[7].AsSpan())");
        Throws<ArgumentOutOfRangeException>("MemoryMarshal.AsRef<long>((ReadOnlySpan<byte>)new byte[7])");
        Expect("AsRefWritesThrough() == 0x12345678");

        // ToEnumerable returns a whole array or string itself and enumerates slices and memory managers.
        var array = new[] { 1, 2, 3, 4 };
        Expect($"ReferenceEquals(MemoryMarshal.ToEnumerable<int>(Numbers), Numbers) == {ReferenceEquals(MemoryMarshal.ToEnumerable<int>(array), array).ToString().ToLowerInvariant()}");
        Expect($"MemoryMarshal.ToEnumerable<int>(Numbers.AsMemory(1, 2)) is int[] == {(MemoryMarshal.ToEnumerable<int>(array.AsMemory(1, 2)) is int[]).ToString().ToLowerInvariant()}");
        Expect($"Join(MemoryMarshal.ToEnumerable<int>(Numbers.AsMemory(1, 2))) == \"{string.Join(',', MemoryMarshal.ToEnumerable<int>(array.AsMemory(1, 2)))}\"");
        Expect($"ReferenceEquals(MemoryMarshal.ToEnumerable<int>(default), Array.Empty<int>()) == {ReferenceEquals(MemoryMarshal.ToEnumerable<int>(default), Array.Empty<int>()).ToString().ToLowerInvariant()}");
        Expect($"ReferenceEquals(MemoryMarshal.ToEnumerable<int>(Numbers.AsMemory(2, 0)), Array.Empty<int>()) == {ReferenceEquals(MemoryMarshal.ToEnumerable<int>(array.AsMemory(2, 0)), Array.Empty<int>()).ToString().ToLowerInvariant()}");
        var text = "abcd";
        Expect($"ReferenceEquals(MemoryMarshal.ToEnumerable<char>(Text.AsMemory()), Text) == {ReferenceEquals(MemoryMarshal.ToEnumerable<char>(text.AsMemory()), text).ToString().ToLowerInvariant()}");
        Expect($"Chars(MemoryMarshal.ToEnumerable<char>(Text.AsMemory(1, 2))) == \"{string.Concat(MemoryMarshal.ToEnumerable<char>(text.AsMemory(1, 2)))}\"");
        Expect("Join(MemoryMarshal.ToEnumerable<int>(new Manager(new[] { 5, 6, 7 }).Memory.Slice(1))) == \"6,7\"");
        Expect("SliceSeesWrites() == \"9,3\"");

        // CreateReadOnlySpanFromNullTerminated.
        Expect("NullTerminatedLengths() == 2002");

        // GetArrayDataReference: empty and rectangular arrays, and the Span and BlockCopy paths that use it.
        var empty = Array.Empty<int>();
        Expect($"Unsafe.IsNullRef(ref MemoryMarshal.GetArrayDataReference(new int[0])) == {Unsafe.IsNullRef(ref MemoryMarshal.GetArrayDataReference(empty)).ToString().ToLowerInvariant()}");
#pragma warning disable CA2265 // The comparison with default is the behavior under test.
        Expect($"(new Span<int>(new int[0]) == default) == {(new Span<int>(empty) == default).ToString().ToLowerInvariant()}");
#pragma warning restore CA2265
        Expect("RectangularData() == 7");

        return $$"""
            using System;
            using System.Buffers;
            using System.Collections.Generic;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            public struct B3 { public byte A, B, C; }
            public struct B5 { public byte A, B, C, D, E; }
            public struct V12 { public int X, Y, Z; }
            public struct Holder { public string Value; }

            public sealed class Manager : MemoryManager<int>
            {
                private readonly int[] _items;
                public Manager(int[] items) => _items = items;
                public override Span<int> GetSpan() => _items;
                public override MemoryHandle Pin(int elementIndex = 0) => default;
                public override void Unpin() { }
                protected override void Dispose(bool disposing) { }
            }

            public static unsafe class EntryPoint
            {
                private static readonly int[] Numbers = { 1, 2, 3, 4 };
                private static readonly string Text = "abcd";
                private static long anchor;
                private static byte byteAnchor;

                private static byte[] Source<TFrom>(int length) where TFrom : struct
                {
                    var bytes = new byte[length * Unsafe.SizeOf<TFrom>()];
                    for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)(i * 7 + 3);
                    return bytes;
                }

                private static int CastLength<TFrom, TTo>(int length) where TFrom : struct where TTo : struct =>
                    MemoryMarshal.Cast<TFrom, TTo>(new TFrom[length].AsSpan()).Length;

                private static int ReadOnlyCastLength<TFrom, TTo>(int length) where TFrom : struct where TTo : struct =>
                    MemoryMarshal.Cast<TFrom, TTo>((ReadOnlySpan<TFrom>)new TFrom[length]).Length;

                private static long CastSum<TFrom, TTo>(int length) where TFrom : struct where TTo : struct
                {
                    var source = MemoryMarshal.Cast<byte, TFrom>(Source<TFrom>(length).AsSpan());
                    var bytes = MemoryMarshal.AsBytes(MemoryMarshal.Cast<TFrom, TTo>(source));
                    long sum = 0;
                    for (var i = 0; i < bytes.Length; i++) sum += (i + 1) * (long)bytes[i];
                    return sum;
                }

                private static byte[] Fill()
                {
                    var buffer = new byte[16];
                    for (var i = 0; i < buffer.Length; i++) buffer[i] = (byte)(i * 37 + 11);
                    return buffer;
                }

                private static long Written()
                {
                    var written = new byte[8];
                    MemoryMarshal.Write(written.AsSpan(1), new B3 { A = 1, B = 2, C = 3 });
                    MemoryMarshal.Write(written.AsSpan(4), 0x0A0B0C);
                    return BitConverter.ToInt64(written);
                }

                private static int AsRefWritesThrough()
                {
                    var bytes = new byte[6];
                    MemoryMarshal.AsRef<int>(bytes.AsSpan(1)) = 0x12345678;
                    return MemoryMarshal.AsRef<int>((ReadOnlySpan<byte>)bytes.AsSpan(1));
                }

                private static string Join(IEnumerable<int> items) => string.Join(",", items);

                private static string Chars(IEnumerable<char> items)
                {
                    var text = "";
                    foreach (var item in items) text += item;
                    return text;
                }

                private static string SliceSeesWrites()
                {
                    var items = new[] { 1, 2, 3 };
                    var enumerable = MemoryMarshal.ToEnumerable<int>(items.AsMemory(1));
                    items[1] = 9;
                    return Join(enumerable);
                }

                private static int NullTerminatedLengths()
                {
                    byte* bytes = stackalloc byte[] { 65, 66, 67, 0, 68 };
                    char* chars = stackalloc char[] { 'x', 'y', '\0' };
                    var empty = MemoryMarshal.CreateReadOnlySpanFromNullTerminated((byte*)null);
                    return MemoryMarshal.CreateReadOnlySpanFromNullTerminated(bytes).Length +
                        MemoryMarshal.CreateReadOnlySpanFromNullTerminated(chars).Length * 1000 - empty.Length -
                        MemoryMarshal.CreateReadOnlySpanFromNullTerminated((char*)null).Length - 1 +
                        (MemoryMarshal.CreateReadOnlySpanFromNullTerminated(bytes)[2] == 67 ? 0 : 100);
                }

                private static int RectangularData()
                {
                    var data = new int[2, 3];
                    data[1, 2] = 7;
                    return Unsafe.Add(ref Unsafe.As<byte, int>(ref MemoryMarshal.GetArrayDataReference((Array)data)), 5);
                }

                public static int Run(int input)
                {
            {{checks}}
                    return 42;
                }
            }
            """;
    }

    private static (int Length, long Sum) CastOnDesktop(string from, string to, int length) => from switch
    {
        "byte" => To<byte>(to, length),
        "short" => To<short>(to, length),
        "B3" => To<B3>(to, length),
        "int" => To<int>(to, length),
        "B5" => To<B5>(to, length),
        "long" => To<long>(to, length),
        _ => To<V12>(to, length),
    };

    private static (int Length, long Sum) To<TFrom>(string to, int length) where TFrom : struct => to switch
    {
        "byte" => Cast<TFrom, byte>(length),
        "short" => Cast<TFrom, short>(length),
        "B3" => Cast<TFrom, B3>(length),
        "int" => Cast<TFrom, int>(length),
        "B5" => Cast<TFrom, B5>(length),
        "long" => Cast<TFrom, long>(length),
        _ => Cast<TFrom, V12>(length),
    };

    private static (int Length, long Sum) Cast<TFrom, TTo>(int length) where TFrom : struct where TTo : struct
    {
        var castLength = MemoryMarshal.Cast<TFrom, TTo>(new TFrom[length].AsSpan()).Length;
        var source = new byte[length * Unsafe.SizeOf<TFrom>()];
        for (var i = 0; i < source.Length; i++) source[i] = (byte)(i * 7 + 3);
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.Cast<TFrom, TTo>(MemoryMarshal.Cast<byte, TFrom>(source.AsSpan())));
        long sum = 0;
        for (var i = 0; i < bytes.Length; i++) sum += (i + 1) * (long)bytes[i];
        return (castLength, sum);
    }
}
