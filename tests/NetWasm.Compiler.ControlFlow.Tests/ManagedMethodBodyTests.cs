using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class ManagedMethodBodyTests
{
    [Fact]
    public void ConstructorPublishesTheCanonicalValidatedBody()
    {
        var (method, controlFlow) = CreateControlFlow();

        var body = new ManagedMethodBody(method, controlFlow);

        Assert.Same(method, body.Method);
        Assert.Same(controlFlow, body.ControlFlow);
        Assert.Same(controlFlow.Graph.MethodBody, body.Body);
    }

    [Fact]
    public void ConstructorRejectsMissingInputs()
    {
        var (method, controlFlow) = CreateControlFlow();

        Assert.Throws<ArgumentNullException>(() => new ManagedMethodBody(null!, controlFlow));
        Assert.Throws<ArgumentNullException>(() => new ManagedMethodBody(method, null!));
    }

    [Fact]
    public void ConstructorRejectsMismatchedDefinitionAndInstanceIdentities()
    {
        var (method, controlFlow) = CreateControlFlow();
        var otherDefinition = method.Definition with
        {
            Key = new EntityKey(method.Definition.Key.Assembly, 2),
        };
        var otherDefinitionMethod = method with { Definition = otherDefinition };
        var otherTypeMethod = method with
        {
            DeclaringType = CliTypeIdentity.Named(
                method.Definition.Key.Assembly,
                "Tests",
                "Other",
                isValueType: false),
        };
        var bodyWithoutInstance = controlFlow.Graph.MethodBody with
        {
            MethodInstance = null,
        };
        var graphWithoutInstance = new ControlFlowGraphBuilderFactory()
            .Create()
            .Build(bodyWithoutInstance);
        var controlFlowWithoutInstance = controlFlow with
        {
            Graph = graphWithoutInstance,
        };

        Assert.Throws<ArgumentException>(() =>
            new ManagedMethodBody(otherDefinitionMethod, controlFlow));
        Assert.Throws<ArgumentException>(() =>
            new ManagedMethodBody(otherTypeMethod, controlFlow));
        Assert.Throws<ArgumentException>(() =>
            new ManagedMethodBody(method, controlFlowWithoutInstance));
    }

    private static (MethodInstanceModel Method, ValidatedControlFlowGraph ControlFlow)
        CreateControlFlow()
    {
        var assembly = new AssemblyIdentity("ManagedMethodBodyTests");
        var definition = new MethodDefinitionModel(
            new EntityKey(assembly, 1),
            new EntityKey(assembly, 3),
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
        return (
            method,
            new ValidatedControlFlowGraph(
                graph,
                ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, []),
                ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty.Add(0, [])));
    }
}
