using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.ControlFlow.Structuring;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class WasmMethodLowererTests
{
    [Fact]
    public void LowerPassesTheValidatedGraphToTheStructuredMethodBuilder()
    {
        var managed = CreateManagedMethodBody();
        var expected = new ValidatedStructuredMethodBuilderFactory()
            .Create()
            .Build(managed.ControlFlow);
        var builder = new RecordingBuilder(expected);
        var factory = new RecordingFactory(builder);
        var lowerer = new WasmMethodLowerer(factory);

        var actual = ((IWasmMethodLowerer)lowerer).Lower(managed);

        Assert.Same(expected, actual);
        Assert.Same(managed.ControlFlow, builder.ControlFlow);
        Assert.Equal(1, factory.CreateCount);
    }

    [Fact]
    public void ConstructorAndLowerRejectMissingInputs()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmMethodLowerer(null!));
        var lowerer = new WasmMethodLowerer(
            new RecordingFactory(new RecordingBuilder(null!)));
        Assert.Throws<ArgumentNullException>(() => lowerer.Lower(null!));
    }

    private static ManagedMethodBody CreateManagedMethodBody()
    {
        var assembly = new AssemblyIdentity("WasmMethodLowererTests");
        var definition = new MethodDefinitionModel(
            new EntityKey(assembly, 1),
            new EntityKey(assembly, 2),
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            0);
        var method = new MethodInstanceModel(
            definition,
            CliTypeIdentity.Named(assembly, "Tests", "Program", isValueType: false),
            [],
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

    private sealed class RecordingFactory(
        IValidatedStructuredMethodBuilder builder) :
        IValidatedStructuredMethodBuilderFactory
    {
        public int CreateCount { get; private set; }

        public IValidatedStructuredMethodBuilder Create()
        {
            CreateCount++;
            return builder;
        }
    }

    private sealed class RecordingBuilder(
        StructuredMethod result) : IValidatedStructuredMethodBuilder
    {
        public ValidatedControlFlowGraph? ControlFlow { get; private set; }

        public StructuredMethod Build(ValidatedControlFlowGraph validated)
        {
            ControlFlow = validated;
            return result;
        }
    }
}
