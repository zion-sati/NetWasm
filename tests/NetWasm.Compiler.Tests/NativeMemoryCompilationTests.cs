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
}
