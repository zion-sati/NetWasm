using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class DelegateFunctionAppenderTests
{
    // Every invoke function comes first, then every multicast function, as the plan
    // numbers them; each invoke function calls its own type's multicast function, which
    // Binaryen must not inline back into it.
    [Fact]
    public void AppendsEveryInvokeThenEveryMulticastFunction()
    {
        var fixture = new Fixture();

        fixture.Appender.Append(
            fixture.Functions,
            [fixture.First, fixture.Second],
            fixture.Target,
            fixture.Indices,
            fixture.FunctionIndices);

        Assert.Equal(
        [
            $"delegate.invoke<{fixture.First.DeclaringType.CanonicalName}>",
            $"delegate.invoke<{fixture.Second.DeclaringType.CanonicalName}>",
            $"delegate.multicast<{fixture.First.DeclaringType.CanonicalName}>",
            $"delegate.multicast<{fixture.Second.DeclaringType.CanonicalName}>",
        ], fixture.Functions.Select(function => function.Name));
        Assert.Equal([false, false, true, true], fixture.Functions.Select(function => function.IsNoInlining));
        Assert.All(fixture.Functions, function => Assert.False(function.IsAggressiveInlining));
        Assert.Equal([(byte)1, (byte)1, (byte)2, (byte)2], fixture.Functions.Select(function => function.Body.Single()));
        Assert.Equal([fixture.First, fixture.Second], fixture.Invokes.Invokes);
        Assert.Equal([new WasmFunctionIndex(52), new WasmFunctionIndex(53)], fixture.Invokes.Multicasts);
        Assert.Equal([fixture.First, fixture.Second], fixture.Multicasts.Invokes);
        Assert.Equal([new WasmFunctionIndex(50), new WasmFunctionIndex(51)], fixture.Multicasts.InvokeFunctions);
        Assert.All(fixture.Invokes.Targets.Concat(fixture.Multicasts.Targets), target => Assert.Same(fixture.Target, target));
        Assert.All(fixture.Invokes.Resolvers.Concat(fixture.Multicasts.Resolvers),
            resolver => Assert.Same(fixture.FunctionIndices, resolver));
    }

    [Fact]
    public void EmptyInvokeSetAppendsNothing()
    {
        var fixture = new Fixture();

        fixture.Appender.Append(
            fixture.Functions,
            [],
            fixture.Target,
            fixture.Indices,
            fixture.FunctionIndices);

        Assert.Empty(fixture.Functions);
        Assert.Empty(fixture.Invokes.Invokes);
        Assert.Empty(fixture.Multicasts.Invokes);
    }

    [Fact]
    public void RejectsMissingFunctionsInvokeTargetIndicesAndResolver()
    {
        var fixture = new Fixture();

        Assert.Throws<ArgumentNullException>(() => fixture.Appender.Append(
            null!, [], fixture.Target, fixture.Indices, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => fixture.Appender.Append(fixture.Functions,
            [], null!, fixture.Indices, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => fixture.Appender.Append(fixture.Functions,
            [], fixture.Target, null!, fixture.FunctionIndices));
        Assert.Throws<ArgumentNullException>(() => fixture.Appender.Append(fixture.Functions,
            [], fixture.Target, fixture.Indices, null!));
    }

    private sealed class Fixture
    {
        public List<WasmFunctionDefinition> Functions { get; } = [];
        public RecordingInvokeEmitter Invokes { get; } = new();
        public RecordingMulticastEmitter Multicasts { get; } = new();
        public IDelegateFunctionAppender Appender { get; }
        public MethodInstanceModel First { get; }
        public MethodInstanceModel Second { get; }
        public DelegateInvokeTarget Target { get; }
        public FunctionIndexMap Indices { get; }
        public IFunctionIndexResolver FunctionIndices { get; }

        public Fixture()
        {
            var program = new FakeProgram();
            var definition = program.GetMethod(EmitterTestSupport.EntryKey);
            First = new(definition,
                CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Tests", "Callback",
                    isValueType: false), [], definition.Signature);
            Second = new(definition,
                CliTypeIdentity.Named(EmitterTestSupport.Assembly, "Tests", "Handler",
                    isValueType: false), [], definition.Signature);
            Target = new([], Lookups.NoEntries<string, StaticInitializerGuard>());
            Indices = new([], [], ImmutableDictionary<string, WasmFunctionIndex>.Empty
                .Add(First.DeclaringType.CanonicalName, new(50))
                .Add(Second.DeclaringType.CanonicalName, new(51)), [])
            {
                DelegateMulticastHelpers = ImmutableDictionary<string, WasmFunctionIndex>.Empty
                    .Add(First.DeclaringType.CanonicalName, new(52))
                    .Add(Second.DeclaringType.CanonicalName, new(53)),
            };
            FunctionIndices = EmitterTestSupport.CreateFunctionIndexResolver(program);
            Appender = Assert.IsAssignableFrom<IDelegateFunctionAppender>(
                new DelegateFunctionAppender(
                    Invokes,
                    Multicasts,
                    new FixedFunctionTypeResolver()));
        }
    }

    private sealed class RecordingInvokeEmitter : IDelegateInvokeFunctionEmitter
    {
        public List<MethodInstanceModel> Invokes { get; } = [];
        public List<DelegateInvokeTarget> Targets { get; } = [];
        public List<IFunctionIndexResolver> Resolvers { get; } = [];
        public List<WasmFunctionIndex> Multicasts { get; } = [];

        public byte[] Emit(MethodInstanceModel invoke, DelegateInvokeTarget targetProgram,
            IFunctionIndexResolver functionIndices, WasmFunctionIndex multicastFunction)
        {
            Invokes.Add(invoke);
            Targets.Add(targetProgram);
            Resolvers.Add(functionIndices);
            Multicasts.Add(multicastFunction);
            return [1];
        }
    }

    private sealed class RecordingMulticastEmitter : IDelegateMulticastFunctionEmitter
    {
        public List<MethodInstanceModel> Invokes { get; } = [];
        public List<DelegateInvokeTarget> Targets { get; } = [];
        public List<IFunctionIndexResolver> Resolvers { get; } = [];
        public List<WasmFunctionIndex> InvokeFunctions { get; } = [];

        public byte[] Emit(MethodInstanceModel invoke, DelegateInvokeTarget targetProgram,
            IFunctionIndexResolver functionIndices, WasmFunctionIndex invokeFunction)
        {
            Invokes.Add(invoke);
            Targets.Add(targetProgram);
            Resolvers.Add(functionIndices);
            InvokeFunctions.Add(invokeFunction);
            return [2];
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
