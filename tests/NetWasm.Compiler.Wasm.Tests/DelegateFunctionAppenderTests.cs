using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class DelegateFunctionAppenderTests
{
    [Fact]
    public void AppendsEveryRequestedInvoke()
    {
        var fixture = new Fixture();

        fixture.Appender.Append(
            fixture.Functions,
            [fixture.Invoke],
            fixture.Target,
            fixture.FunctionIndices);

        Assert.Equal($"delegate.invoke<{fixture.Invoke.DeclaringType.CanonicalName}>",
            Assert.Single(fixture.Functions).Name);
        Assert.Equal(1, fixture.Invokes.Count);
    }

    [Fact]
    public void EmptyInvokeSetAppendsNothing()
    {
        var fixture = new Fixture();

        fixture.Appender.Append(
            fixture.Functions,
            [],
            fixture.Target,
            fixture.FunctionIndices);

        Assert.Empty(fixture.Functions);
        Assert.Equal(0, fixture.Invokes.Count);
    }

    [Fact]
    public void RejectsMissingFunctionsInvokeTargetAndResolver()
    {
        var fixture = new Fixture();

        Assert.Throws<ArgumentNullException>(() => fixture.Appender.Append(
            null!, [], fixture.Target, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => fixture.Appender.Append(fixture.Functions,
            [], null!, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => fixture.Appender.Append(fixture.Functions,
            [], fixture.Target, null!));
    }

    private sealed class Fixture
    {
        public List<WasmFunctionDefinition> Functions { get; } = [];
        public RecordingDelegateInvokeEmitter Invokes { get; } = new();
        public IDelegateFunctionAppender Appender { get; }
        public MethodInstanceModel Invoke { get; }
        public DelegateInvokeTarget Target { get; }
        public IFunctionIndexResolver FunctionIndices { get; }

        public Fixture()
        {
            var program = new FakeProgram();
            var definition = program.GetMethod(EmitterTestSupport.EntryKey);
            Invoke = new(definition,
                CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Tests", "Callback",
                    isValueType: false), [], definition.Signature);
            Target = new([], Lookups.NoEntries<string, StaticInitializerGuard>());
            FunctionIndices = EmitterTestSupport.CreateFunctionIndexResolver(program);
            Appender = Assert.IsAssignableFrom<IDelegateFunctionAppender>(
                new DelegateFunctionAppender(
                    Invokes,
                    new FixedFunctionTypeResolver()));
        }
    }

    private sealed class RecordingDelegateInvokeEmitter : IDelegateInvokeFunctionEmitter
    {
        public int Count { get; private set; }

        public byte[] Emit(MethodInstanceModel invoke, DelegateInvokeTarget targetProgram,
            IFunctionIndexResolver functionIndices)
        {
            Count++;
            return [1];
        }
    }

    private sealed class FixedFunctionTypeResolver : IManagedMethodFunctionTypeResolver
    {
        public WasmFunctionType Resolve(MethodDefinitionModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);

        public WasmFunctionType Resolve(MethodInstanceModel method) =>
            WasmFunctionType.Create(CliValueKind.I4);
    }
}
