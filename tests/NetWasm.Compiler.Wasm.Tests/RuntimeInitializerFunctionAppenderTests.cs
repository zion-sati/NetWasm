using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class RuntimeInitializerFunctionAppenderTests
{
    [Fact]
    public void AppendsOneNeverInlinedInitializerAndReturnsItsIndex()
    {
        var plan = TestRuntimeInitialization.Create(512);
        var emitter = new RecordingEmitter();
        List<WasmFunctionDefinition> functions =
        [
            new("existing", WasmFunctionType.Create(CliValueKind.Void), [0, 0x0b]),
        ];

        var index = new RuntimeInitializerFunctionAppender(emitter).Append(functions, 7, plan);

        Assert.Equal(8, index);
        Assert.Same(plan, Assert.Single(emitter.Plans));
        var initializer = functions[^1];
        Assert.Equal("runtime.initialize", initializer.Name);
        Assert.Equal(WasmFunctionType.Create(CliValueKind.Void), initializer.Type);
        Assert.Equal(RecordingEmitter.Body, initializer.Body);
        Assert.True(initializer.IsNoInlining);
        Assert.False(initializer.IsAggressiveInlining);
    }

    private sealed class RecordingEmitter : IRuntimeInitializerFunctionEmitter
    {
        public static readonly byte[] Body = [0, 0x0b];

        public List<RuntimeInitializationPlan> Plans { get; } = [];

        public byte[] Emit(RuntimeInitializationPlan plan)
        {
            Plans.Add(plan);
            return Body;
        }
    }
}
