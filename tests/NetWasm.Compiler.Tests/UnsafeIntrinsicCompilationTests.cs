using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

// CoreLib's Unsafe is upstream's: its intrinsics throw or are written for
// runtimes that expand the call, and the compiler gives each the body
// NativeAOT gives it. Every member, in each overload, computes what desktop
// .NET 11 computes, unoptimized and at O3.
public sealed class UnsafeIntrinsicCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;

        namespace UnsafeIntrinsics;

        public struct Pair
        {
            public int A;
            public int B;
        }

        public static unsafe class EntryPoint
        {
            private static int Mix(int hash, long value) => unchecked(hash * 31 + (int)value ^ (int)(value >> 32));

            public static int Run(int input)
            {
                var hash = 17;
                var values = new int[] { input, input + 1, input + 2, input + 3, input + 4, input + 5, input + 6, input + 7 };
                ref var first = ref values[0];

                // Element and byte offsets in each width, forward and back.
                ref var third = ref Unsafe.Add(ref first, 2);
                ref var fourth = ref Unsafe.Add(ref first, (nint)3);
                ref var fifth = ref Unsafe.Add(ref first, (nuint)4);
                ref var second = ref Unsafe.Subtract(ref third, 1);
                ref var backNative = ref Unsafe.Subtract(ref fifth, (nint)2);
                ref var backUnsigned = ref Unsafe.Subtract(ref fifth, (nuint)4);
                ref var byOffset = ref Unsafe.AddByteOffset(ref first, (nint)8);
                ref var byOffsetUnsigned = ref Unsafe.AddByteOffset(ref first, (nuint)12);
                ref var byOffsetBack = ref Unsafe.SubtractByteOffset(ref fifth, (nint)4);
                ref var byOffsetBackUnsigned = ref Unsafe.SubtractByteOffset(ref fifth, (nuint)16);
                hash = Mix(hash, third + fourth * 3 + fifth * 5 + second * 7 + backNative * 11 + backUnsigned * 13 +
                    byOffset * 17 + byOffsetUnsigned * 19 + byOffsetBack * 23 + byOffsetBackUnsigned * 29);
                hash = Mix(hash, Unsafe.ByteOffset(ref first, ref fifth));

                // Comparisons of references.
                hash = Mix(hash, (Unsafe.AreSame(ref third, ref byOffset) ? 1 : 0) | (Unsafe.AreSame(ref first, ref third) ? 2 : 0) |
                    (Unsafe.IsAddressGreaterThan(ref third, ref first) ? 4 : 0) |
                    (Unsafe.IsAddressGreaterThanOrEqualTo(ref third, ref byOffset) ? 8 : 0) |
                    (Unsafe.IsAddressLessThan(ref first, ref third) ? 16 : 0) |
                    (Unsafe.IsAddressLessThanOrEqualTo(ref fifth, ref first) ? 32 : 0));
                hash = Mix(hash, (Unsafe.IsNullRef(ref Unsafe.NullRef<int>()) ? 1 : 0) | (Unsafe.IsNullRef(ref first) ? 2 : 0));

                // Reinterpreted and unaligned access through references.
                var wide = Unsafe.ReadUnaligned<long>(ref Unsafe.As<int, byte>(ref Unsafe.Add(ref first, 1)));
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref Unsafe.As<int, byte>(ref first), 3), (short)(input * 1000 + 7));
                hash = Mix(hash, wide);
                hash = Mix(hash, values[0]);
                hash = Mix(hash, values[1]);

                // Objects: a box's value in place and a reinterpreted reference.
                object boxed = input * 3;
                hash = Mix(hash, Unsafe.Unbox<int>(boxed));
                Unsafe.Unbox<int>(boxed) = 5;
                hash = Mix(hash, (int)boxed);
                object text = "netwasm" + input;
                hash = Mix(hash, Unsafe.As<string>(text).Length);

                // Sizes and bit casts.
                hash = Mix(hash, Unsafe.SizeOf<Pair>() * 100 + Unsafe.SizeOf<decimal>());
                hash = Mix(hash, Unsafe.BitCast<double, long>(input + 0.5));

                // Pointers: reads, writes, copies, fills and pointer arithmetic.
                var buffer = new byte[64];
                fixed (byte* start = buffer)
                {
                    Unsafe.Write(start, input * 7);
                    Unsafe.WriteUnaligned(start + 5, input * 0x1_0000_0001L);
                    hash = Mix(hash, Unsafe.Read<int>(start));
                    hash = Mix(hash, Unsafe.ReadUnaligned<long>(start + 5));
                    var pair = new Pair { A = input, B = -input };
                    Unsafe.Copy(start + 16, ref pair);
                    Pair copied = default;
                    Unsafe.Copy(ref copied, start + 16);
                    hash = Mix(hash, copied.A * 3 + copied.B);
                    Unsafe.CopyBlock(start + 24, start + 16, 8);
                    Unsafe.CopyBlockUnaligned(start + 33, start + 24, 7);
                    Unsafe.CopyBlock(ref buffer[40], ref buffer[33], 4);
                    Unsafe.CopyBlockUnaligned(ref buffer[45], ref buffer[40], 3);
                    Unsafe.InitBlock(start + 50, (byte)(input + 1), 3);
                    Unsafe.InitBlockUnaligned(start + 53, 0xAB, 2);
                    Unsafe.InitBlock(ref buffer[56], (byte)(input * 5), 3);
                    Unsafe.InitBlockUnaligned(ref buffer[59], 0xCD, 5);
                    ref var view = ref Unsafe.AsRef<long>(start + 8);
                    view += input;
                    var next = Unsafe.Add<int>(start, 3);
                    var previous = Unsafe.Subtract<short>(next, 2);
                    hash = Mix(hash, ((byte*)next - start) * 100 + ((byte*)previous - start));
                    hash = Mix(hash, (byte*)Unsafe.AsPointer(ref buffer[10]) - start);
                }
                foreach (var value in buffer) hash = unchecked(hash * 31 + value);

                // A readonly reference made writable, and an uninitialized out.
                ref readonly var readOnly = ref values[2];
                ref var writable = ref Unsafe.AsRef(in readOnly);
                writable += 100;
                hash = Mix(hash, values[2]);
                Unsafe.SkipInit(out Pair skipped);
                skipped.A = input;
                skipped.B = 2;
                return Mix(hash, skipped.A + skipped.B);
            }
        }

        """;

    [Theory]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    public void EveryUnsafeMemberComputesDesktopsResult(WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedUnsafeSource("UnsafeIntrinsics", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "UnsafeIntrinsics.EntryPoint", "Run", [],
            Target: target, Optimization: preset));

        // As desktop .NET 11 computes them.
        foreach (var (input, expected) in new[] { (0, 1514558881), (1, -1592101462), (7, 48535775), (-3, -1535353468) })
        {
            Assert.Equal(expected,
                ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, input, target, result.StaticDataEnd));
        }
    }
}
