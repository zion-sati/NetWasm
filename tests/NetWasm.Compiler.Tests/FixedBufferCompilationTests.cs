using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class FixedBufferCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerExecutesFixedBufferIndexingAcrossEveryProfile(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);

        Assert.Equal(48, executor.Execute(new CompilationScenario(
            "FixedBufferFixture",
            Source,
            "FixedBufferFixture.EntryPoint",
            optimize,
            target,
            41,
            [])
        {
            AllowUnsafe = true,
        }));
    }

    private const string Source = """
        namespace FixedBufferFixture;

        public unsafe struct Buffer
        {
            private fixed uint _values[8];

            public void Set(int index, uint value) => _values[index] = value;

            public uint Get(int index) => _values[index];
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                Buffer buffer = default;
                buffer.Set(0, 7);
                buffer.Set(7, (uint)input);
                return (int)(buffer.Get(0) + buffer.Get(7));
            }
        }
        """;
}
