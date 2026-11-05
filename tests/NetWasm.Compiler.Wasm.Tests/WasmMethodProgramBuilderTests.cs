using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Optimization;
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
        var builder = new WasmMethodProgramBuilder(lowerer, new IndexedWorkExecutor(), new CompilerParallelism(1));

        var result = ((IWasmMethodProgramBuilder)builder).Build(
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty.Add(
                definition.Method.Definition.Key,
                definition),
            ImmutableDictionary<string, ManagedMethodBody>.Empty.Add(
                constructed.Method.CanonicalName,
                constructed),
            BranchPrefixDuplicationPolicy.Speed);

        Assert.Same(definition.Method,
            result.Methods[definition.Method.Definition.Key].Header.MethodInstance);
        Assert.Equal(definition.Method.Definition.Key,
            result.Methods.Single().Value.Header.Method.Key);
        Assert.Equal(constructed.Method.CanonicalName,
            result.ConstructedMethods.Single().Value.Header.MethodInstance!.CanonicalName);
        Assert.Equal([definition, constructed], lowerer.Inputs);
        Assert.Equal(
            [BranchPrefixDuplicationPolicy.Speed,
                BranchPrefixDuplicationPolicy.Speed],
            lowerer.Policies);
    }

    [Fact]
    public void ConstructorAndBuildRejectMissingInputs()
    {
        var work = new IndexedWorkExecutor();
        var parallelism = new CompilerParallelism(1);
        Assert.Throws<ArgumentNullException>(() => new WasmMethodProgramBuilder(null!, work, parallelism));
        Assert.Throws<ArgumentNullException>(() =>
            new WasmMethodProgramBuilder(new RecordingLowerer(), null!, parallelism));
        Assert.Throws<ArgumentNullException>(() =>
            new WasmMethodProgramBuilder(new RecordingLowerer(), work, null!));
        var builder = new WasmMethodProgramBuilder(new RecordingLowerer(), work, parallelism);
        Assert.Throws<ArgumentNullException>(() => builder.Build(
            null!,
            ImmutableDictionary<string, ManagedMethodBody>.Empty));
        Assert.Throws<ArgumentNullException>(() => builder.Build(
            ImmutableDictionary<EntityKey, ManagedMethodBody>.Empty,
            null!));
    }

    [Fact]
    public void WorkersLowerEveryMethodIntoItsOwnEntry()
    {
        var methods = Enumerable.Range(1, 40)
            .Select(token => CreateManagedMethodBody(token, []))
            .ToImmutableDictionary(body => body.Method.Definition.Key);
        var constructed = Enumerable.Range(41, 20)
            .Select(token => CreateManagedMethodBody(token, [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]))
            .ToImmutableDictionary(body => body.Method.CanonicalName, StringComparer.Ordinal);
        var builder = new WasmMethodProgramBuilder(
            new ConcurrentLowerer(), new IndexedWorkExecutor(), new CompilerParallelism(4));

        var result = builder.Build(methods, constructed);

        Assert.All(methods, pair => Assert.Same(pair.Value.Method, result.Methods[pair.Key].Header.MethodInstance));
        Assert.All(constructed, pair =>
            Assert.Same(pair.Value.Method, result.ConstructedMethods[pair.Key].Header.MethodInstance));
    }

    [Fact]
    public void WorkersReportTheFirstFailureInMethodOrder()
    {
        var methods = Enumerable.Range(1, 30)
            .Select(token => CreateManagedMethodBody(token, []))
            .ToImmutableDictionary(body => body.Method.Definition.Key);
        var order = methods.Keys.ToArray();
        var failing = new HashSet<EntityKey> { order[7], order[21] };
        var builder = new WasmMethodProgramBuilder(
            new ConcurrentLowerer(failing), new IndexedWorkExecutor(), new CompilerParallelism(4));

        var failure = Assert.Throws<InvalidOperationException>(() =>
            builder.Build(methods, ImmutableDictionary<string, ManagedMethodBody>.Empty));

        Assert.Equal(order[7].ToString(), failure.Message);
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

    private sealed class ConcurrentLowerer(IReadOnlySet<EntityKey>? failing = null) : IWasmMethodLowerer
    {
        public StructuredMethod Lower(
            ManagedMethodBody method,
            BranchPrefixDuplicationPolicy branchPrefixDuplication =
                BranchPrefixDuplicationPolicy.SizeAware,
            DispatcherEliminationPolicy dispatcherElimination =
                DispatcherEliminationPolicy.Disabled) =>
            failing?.Contains(method.Method.Definition.Key) == true
                ? throw new InvalidOperationException(method.Method.Definition.Key.ToString())
                : new ValidatedStructuredMethodBuilderFactory()
                    .Create(branchPrefixDuplication)
                    .Build(method.ControlFlow);
    }

    private sealed class RecordingLowerer : IWasmMethodLowerer
    {
        public List<ManagedMethodBody> Inputs { get; } = [];

        public List<BranchPrefixDuplicationPolicy> Policies { get; } = [];

        public StructuredMethod Lower(
            ManagedMethodBody method,
            BranchPrefixDuplicationPolicy branchPrefixDuplication =
                BranchPrefixDuplicationPolicy.SizeAware,
            DispatcherEliminationPolicy dispatcherElimination =
                DispatcherEliminationPolicy.Disabled)
        {
            Inputs.Add(method);
            Policies.Add(branchPrefixDuplication);
            return new ValidatedStructuredMethodBuilderFactory()
                .Create(branchPrefixDuplication)
                .Build(method.ControlFlow);
        }
    }
}
