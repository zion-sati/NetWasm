using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class StaticInitializerFunctionAppenderTests
{
    [Fact]
    public void EmptyPlanAddsNoFunctionsAndNonemptyPlanSharesOneFailureFunction()
    {
        var initializers = new RecordingInitializers();
        var failures = new RecordingFailures();
        var appender = Assert.IsAssignableFrom<IStaticInitializerFunctionAppender>(new StaticInitializerFunctionAppender(initializers, failures));
        var functions = new List<WasmFunctionDefinition>();
        appender.Append(functions, null);
        Assert.Empty(functions);
        Assert.Empty(initializers.Requests);
        Assert.Null(failures.Request);
        var plan = new StaticInitializerFunctionPlan([
            new("A", 100, 70, 240), new("B", 101, 71, 244)], 480, 102);

        appender.Append(functions, plan);

        Assert.Equal(plan.Initializers, initializers.Requests);
        Assert.Same(plan, failures.Request);
        Assert.Equal(3, functions.Count);
        Assert.Equal([CliValueKind.ManagedAddress, CliValueKind.ManagedReference], functions[2].Type.Parameters.ToArray());
        Assert.All(functions, function => Assert.Equal(CliValueKind.Void, function.Type.Result));
    }

    private sealed class RecordingInitializers : IStaticInitializerFunctionEmitter
    {
        public List<StaticInitializerFunction> Requests { get; } = [];
        public byte[] Emit(StaticInitializerFunction initializer, StaticInitializerFunctionPlan plan)
        {
            Requests.Add(initializer);
            return [1];
        }
    }
    private sealed class RecordingFailures : IStaticInitializationFailureEmitter
    {
        public StaticInitializerFunctionPlan? Request { get; private set; }
        public byte[] Emit(StaticInitializerFunctionPlan plan)
        {
            Request = plan;
            return [2];
        }
    }
}
