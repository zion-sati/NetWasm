using NetWasm.Compiler.ComponentModel;

namespace NetWasm.Compiler.Tests.Correctness;

// At the speed presets value copies of up to 64 bytes run as inline chunks
// rather than memory.copy: every byte of every size around the chunk widths
// must survive each path a value is copied through. Copies between partially
// overlapping instances are unspecified (desktop's JIT copies them chunk by
// chunk), so the emitter tests pin their memmove order instead.
[Collection(CorrectnessTestGroup.Name)]
public sealed class StructCopyCorpusTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void StructCopiesKeepEveryByteAcrossSizesAndPaths() =>
        runner.Run(new(
            "StructCopyCorpus",
            "NetWasm.Correctness.StructCopies",
            """
            using System;
            using System.Runtime.CompilerServices;
            using System.Runtime.InteropServices;

            namespace NetWasm.Correctness.StructCopies;

            [StructLayout(LayoutKind.Explicit, Size = 3)] public struct S3 { [FieldOffset(0)] public byte A; }
            [StructLayout(LayoutKind.Explicit, Size = 7)] public struct S7 { [FieldOffset(0)] public byte A; }
            [StructLayout(LayoutKind.Explicit, Size = 12)] public struct S12 { [FieldOffset(0)] public byte A; }
            [StructLayout(LayoutKind.Explicit, Size = 17)] public struct S17 { [FieldOffset(0)] public byte A; }
            [StructLayout(LayoutKind.Explicit, Size = 24)] public struct S24 { [FieldOffset(0)] public byte A; }
            [StructLayout(LayoutKind.Explicit, Size = 29)] public struct S29 { [FieldOffset(0)] public byte A; }
            [StructLayout(LayoutKind.Explicit, Size = 33)] public struct S33 { [FieldOffset(0)] public byte A; }
            [StructLayout(LayoutKind.Explicit, Size = 48)] public struct S48 { [FieldOffset(0)] public byte A; }
            [StructLayout(LayoutKind.Explicit, Size = 63)] public struct S63 { [FieldOffset(0)] public byte A; }
            [StructLayout(LayoutKind.Explicit, Size = 64)] public struct S64 { [FieldOffset(0)] public byte A; }
            [StructLayout(LayoutKind.Explicit, Size = 65)] public struct S65 { [FieldOffset(0)] public byte A; }

            public sealed class Holder<T> where T : struct { public T Value; public int After = 0x5A5A5A5A; }

            public static class Statics<T> where T : struct { public static T Value; }

            public static class EntryPoint
            {
                public static int Run(int input) => input switch
                {
                    0 => Paths<S3>(1) ^ Paths<S7>(2) ^ Paths<S12>(3),
                    1 => Paths<S17>(4) ^ Paths<S24>(5) ^ Paths<S29>(6),
                    2 => Paths<S33>(7) ^ Paths<S48>(8),
                    _ => Paths<S63>(9) ^ Paths<S64>(10) ^ Paths<S65>(11),
                };

                private static int Paths<T>(byte seed) where T : struct
                {
                    var value = Filled<T>(seed);
                    var hash = Hash(ref value);

                    var local = value;
                    hash = Mix(hash, Hash(ref local));
                    var argument = Through(value);
                    hash = Mix(hash, Hash(ref argument));

                    var array = new T[3];
                    array[1] = value;
                    var element = array[1];
                    hash = Mix(hash, Hash(ref element) ^ Hash(ref array[0]) ^ Hash(ref array[2]));

                    var holder = new Holder<T> { Value = value };
                    var field = holder.Value;
                    hash = Mix(hash, Hash(ref field) ^ holder.After);

                    Statics<T>.Value = value;
                    var fromStatic = Statics<T>.Value;
                    hash = Mix(hash, Hash(ref fromStatic));

                    object boxed = value;
                    var unboxed = (T)boxed;
                    hash = Mix(hash, Hash(ref unboxed));

                    object nullableBox = (T?)value;
                    var fromNullable = ((T?)nullableBox).GetValueOrDefault();
                    hash = Mix(hash, Hash(ref fromNullable));

                    Span<T> span = array;
                    span[0] = value;
                    var fromSpan = span[0];
                    hash = Mix(hash, Hash(ref fromSpan));
                    foreach (var item in span)
                    {
                        var copy = item;
                        hash = Mix(hash, Hash(ref copy));
                    }

                    var grid = new T[2, 2];
                    grid[1, 0] = value;
                    var cell = grid[1, 0];
                    return Mix(hash, Hash(ref cell) ^ Hash(ref grid[0, 1]));
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static T Through<T>(T value) where T : struct => value;

                private static T Filled<T>(byte seed) where T : struct
                {
                    T value = default;
                    var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1));
                    for (var index = 0; index < bytes.Length; index++) bytes[index] = (byte)(seed * 31 + index * 7 + 1);
                    return value;
                }

                private static int Hash<T>(ref T value) where T : struct
                {
                    var hash = 17;
                    foreach (var b in MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1)))
                        hash = unchecked(hash * 31 + b);
                    return hash;
                }

                private static int Mix(int left, int right) => unchecked(left * 397 ^ right);

                public static int Trace() => 0;
            }
            """,
            [0, 1, 2, 3])
        {
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            Optimization = FinalWasmOptimization.O3,
        });
}
