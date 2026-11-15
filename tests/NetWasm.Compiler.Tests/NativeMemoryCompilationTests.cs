// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class NativeMemoryCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesCanonicalByteAccessForEveryTargetAndOptimization(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "CanonicalByteAccessFixture",
            CanonicalByteAccessSource,
            "CanonicalByteAccessFixture.EntryPoint",
            optimize,
            target,
            0,
            [])
        {
            AllowUnsafe = true,
        });

        Assert.Equal(63, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerValidatesNativeMemoryForEveryTargetAndOptimization(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "NativeMemoryFixture",
            Source,
            "NativeMemoryFixture.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            AllowUnsafe = true,
        });

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesBulkAndZeroSizeNativeMemoryContracts(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            "NativeMemoryBulkFixture",
            BulkSource,
            "NativeMemoryBulkFixture.EntryPoint",
            optimize,
            target,
            0,
            [])
        {
            AllowUnsafe = true,
        });

        Assert.Equal(42, result);
    }

    private const string Source = """
        namespace NativeMemoryFixture;

        public static unsafe class EntryPoint
        {
            public static int Run(int input)
            {
                var bytes = (byte*)System.Runtime.InteropServices.NativeMemory.Alloc(4);
                System.Runtime.InteropServices.NativeMemory.Fill(bytes, 4, 1);
                bytes[0] = (byte)input;
                bytes = (byte*)System.Runtime.InteropServices.NativeMemory.Realloc(bytes, 8);

                var copy = (byte*)System.Runtime.InteropServices.NativeMemory.AlignedAlloc(8, 8);
                System.Runtime.InteropServices.NativeMemory.Copy(bytes, copy, 4);
                var result = copy[0] + copy[1];

                System.Runtime.InteropServices.NativeMemory.AlignedFree(copy);
                System.Runtime.InteropServices.NativeMemory.Free(bytes);
                try
                {
                    _ = System.Runtime.InteropServices.NativeMemory.AlignedAlloc(8, 3);
                    return -1;
                }
                catch (System.ArgumentException)
                {
                    return result;
                }
            }
        }
        """;

    private const string CanonicalByteAccessSource = """
        namespace CanonicalByteAccessFixture;

        public static unsafe class EntryPoint
        {
            public static int Run(int input)
            {
                var bytes = (byte*)System.Runtime.InteropServices.NativeMemory.Alloc(2);
                bytes[0] = 17;
                bytes[1] = 23;
                var address = unchecked((nuint)bytes);
                var result = System.Runtime.InteropServices.WebAssembly.CanonicalAbi.ReadByte(
                    address,
                    0) == 17 ? 1 : 0;
                System.Runtime.InteropServices.WebAssembly.CanonicalAbi.WriteByte(
                    address,
                    1,
                    29);
                result |= bytes[1] == 29 ? 2 : 0;
                var sum = 0;
                for (var index = 0; index < 2; index++)
                {
                    sum += System.Runtime.InteropServices.WebAssembly.CanonicalAbi.ReadByte(
                        address,
                        unchecked((nuint)index));
                }
                result |= sum == 46 ? 4 : 0;
                var offset = unchecked((nuint)1);
                System.Runtime.InteropServices.WebAssembly.CanonicalAbi.WriteByte(
                    address,
                    offset,
                    31);
                result |= bytes[1] == 31 ? 8 : 0;
                result |= System.Runtime.InteropServices.WebAssembly.CanonicalAbi.ReadByte(
                    address,
                    offset) == 31 ? 16 : 0;
                result |= bytes[0] == 17 ? 32 : 0;
                System.Runtime.InteropServices.NativeMemory.Free(bytes);
                return result;
            }
        }
        """;

    internal const string BulkSource = """
        using System;
        using System.Runtime.InteropServices;

        namespace NativeMemoryBulkFixture;

        public static unsafe class EntryPoint
        {
            public static int Trace() => 0;

            public static int Run(int input)
            {
                NativeMemory.Clear(null, 0);
                NativeMemory.Fill(null, 0, 0xA5);
                NativeMemory.Copy(null, null, 0);
                NativeMemory.Free(null);
                NativeMemory.AlignedFree(null);

                var empty = NativeMemory.Alloc(0);
                if (empty == null) return -1;
                empty = NativeMemory.Realloc(empty, 0);
                if (empty == null) return -2;
                NativeMemory.Free(empty);
                empty = NativeMemory.Realloc(null, 0);
                if (empty == null) return -3;
                NativeMemory.Free(empty);

                var emptyZeroed = NativeMemory.AllocZeroed(0);
                if (emptyZeroed == null) return -4;
                NativeMemory.Free(emptyZeroed);
                emptyZeroed = NativeMemory.AllocZeroed(0, 0);
                if (emptyZeroed == null) return -5;
                NativeMemory.Free(emptyZeroed);

                var aligned = NativeMemory.AlignedAlloc(0, 16);
                if (aligned == null || (nuint)aligned % 16 != 0) return -6;
                aligned = NativeMemory.AlignedRealloc(aligned, 0, 16);
                if (aligned == null || (nuint)aligned % 16 != 0) return -7;
                NativeMemory.AlignedFree(aligned);
                aligned = NativeMemory.AlignedRealloc(null, 0, 16);
                if (aligned == null || (nuint)aligned % 16 != 0) return -8;
                NativeMemory.AlignedFree(aligned);

                if (input != 0)
                {
                    try
                    {
                        _ = NativeMemory.AllocZeroed(nuint.MaxValue, 2);
                        return -9;
                    }
                    catch (OutOfMemoryException)
                    {
                    }
                }

                var bytes = (byte*)NativeMemory.AllocZeroed(16);
                for (var index = 0; index < 16; index++)
                {
                    if (bytes[index] != 0) return -10;
                }

                NativeMemory.Fill(bytes, 16, 0xA5);
                NativeMemory.Clear(bytes + 4, 4);
                for (var index = 0; index < 16; index++)
                {
                    var expected = index is >= 4 and < 8 ? 0 : 0xA5;
                    if (bytes[index] != expected) return -11;
                }

                for (var index = 0; index < 8; index++)
                {
                    bytes[index] = (byte)(index + 1);
                }
                NativeMemory.Copy(bytes, bytes + 1, 7);
                if (!Matches(bytes, 1, 1, 2, 3, 4, 5, 6, 7)) return -12;
                NativeMemory.Copy(bytes + 1, bytes, 7);
                if (!Matches(bytes, 1, 2, 3, 4, 5, 6, 7, 7)) return -13;

                NativeMemory.Free(bytes);
                return 42;
            }

            private static bool Matches(
                byte* values,
                byte first,
                byte second,
                byte third,
                byte fourth,
                byte fifth,
                byte sixth,
                byte seventh,
                byte eighth) =>
                values[0] == first && values[1] == second &&
                values[2] == third && values[3] == fourth &&
                values[4] == fifth && values[5] == sixth &&
                values[6] == seventh && values[7] == eighth;
        }
        """;
}
