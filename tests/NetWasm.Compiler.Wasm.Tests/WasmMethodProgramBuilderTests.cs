using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmMethodProgramBuilderTests
{
    [Fact]
    public void BuildLowersDefinitionAndConstructedMethodsIntoSeparateImmutableMaps()
    {
        var definition = CreateManagedMethodBody(1, []);
        var constructed = CreateManagedMethodBody(
            2,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var lowerer = new RecordingLowerer();
        var builder = new WasmMethodProgramBuilder(lowerer);

        var result = ((IWasmMethodProgramBuilder)builder).Build(
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(
                definition.Method.Definition.Key,
                definition),
            ImmutableDictionary<string, ManagedMethodBody>.Empty.Add(
                constructed.Method.CanonicalName,
                constructed));

        Assert.Same(definition.Method,
            result.Methods[definition.Method.Definition.Key].Header.MethodInstance);
        Assert.Equal(definition.Method.Definition.Key,
            result.Methods.Single().Value.Header.Method.Key);
        Assert.Equal(constructed.Method.CanonicalName,
            result.ConstructedMethods.Single().Value.Header.MethodInstance!.CanonicalName);
        Assert.Equal([definition, constructed], lowerer.Inputs);
    }

    [Fact]
    public void ConstructorAndBuildRejectMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmMethodProgramBuilder(null!));
        var builder = new WasmMethodProgramBuilder(new RecordingLowerer());
        Assert.Throws<ArgumentNullException>(() => builder.Build(
            null!,
            ImmutableDictionary<string, ManagedMethodBody>.Empty));
        Assert.Throws<ArgumentNullException>(() => builder.Build(
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty,
            null!));
    }

    private static ManagedMethodBody CreateManagedMethodBody(
        int token,
        ImmutableArray<CliTypeIdentity> methodArguments)
    {
        var assembly = new AssemblyIdentity("WasmMethodProgramBuilderTests");
        var definition = new MethodDefinitionModel(
            new EntityKey(assembly, token),
            new EntityKey(assembly, 100),
            $"Method{token}",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(assembly, "Tests", "Program", isValueType: false),
            methodArguments,
            definition.Signature);
        var body = new CilMethodBody(
            definition,
            0,
            [],
            [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())])
        {
            MethodInstance = method,
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var controlFlow = new ValidatedControlFlowGraph(
            graph,
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, []),
            ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, []));
        return new ManagedMethodBody(method, controlFlow);
    }

    private sealed class RecordingLowerer : IWasmMethodLowerer
    {
        public List<ManagedMethodBody> Inputs { get; } = [];

        public StructuredMethod Lower(ManagedMethodBody method)
        {
            Inputs.Add(method);
            return new ValidatedStructuredMethodBuilderFactory()
                .Create()
                .Build(method.ControlFlow);
        }
    }
}
