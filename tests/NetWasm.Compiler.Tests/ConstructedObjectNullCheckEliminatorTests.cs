using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.NullChecks;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Tests;

public sealed class ConstructedObjectNullCheckEliminatorTests
{
    private static readonly CilMethodBody Body = ValidationTestData.ManagedMethodBody().Body;

    // The planner's offsets join the body's proof; a disabled pass, or a plan with
    // nothing in it, leaves the body as it was.
    [Theory]
    [InlineData(true, new[] { 0 })]
    [InlineData(false, new[] { 0 })]
    [InlineData(true, new int[0])]
    public void AddsThePlannedOffsetsToTheBodysProof(bool enabled, int[] planned)
    {
        var eliminator = new ConstructedObjectNullCheckEliminator(new ControlFlowGraphBuilderFactory().Create(),
            new PassThrough(), new FixedPlan([.. planned]), enabled);

        var result = eliminator.Rewrite(Body);

        if (!enabled || planned.Length == 0)
        {
            Assert.Same(Body, result);
            return;
        }
        Assert.Equal(planned, result.ArrayBoundsProof!.NonNullOffsets);
    }

    // A proof of this snapshot gains the offsets; one of an earlier snapshot is replaced.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AProofOfAnotherSnapshotIsReplaced(bool stale)
    {
        var proof = new ArrayBoundsProof(Body.Instructions, Body.Locals, Body.LocalSignatureTypes, [7])
        {
            NonNullOffsets = [5],
        };
        var body = Body with { ArrayBoundsProof = stale ? proof with { Instructions = [] } : proof };

        var result = NonNullProof.Add(body, [0]).ArrayBoundsProof!;

        Assert.Equal(stale ? [0] : [0, 5], result.NonNullOffsets.Order());
        Assert.Equal(stale ? [] : [7], result.AccessOffsets);
        Assert.True(result.Describes(Body.Instructions, Body.Locals, Body.LocalSignatureTypes));
    }

    // Which references are proven non-null can change with the rest of the program, as
    // a constructor's this does, so a compiled unit's fingerprint includes them.
    [Fact]
    public void TheFingerprintCoversTheProvenOffsets()
    {
        var method = ValidationTestData.ManagedMethodBody();
        var proven = With(method, NonNullProof.Add(method.Body, [0]));
        var empty = With(method, NonNullProof.Add(method.Body, []));

        Assert.True(Fingerprint(proven) != Fingerprint(empty));
        Assert.True(Fingerprint(empty) != Fingerprint(method));
    }

    [Fact]
    public void RejectsMissingDependenciesAndInput()
    {
        var graphs = new ControlFlowGraphBuilderFactory().Create();
        var plan = new FixedPlan([]);
        Assert.Throws<ArgumentNullException>(() => new ConstructedObjectNullCheckEliminator(null!, new PassThrough(), plan, true));
        Assert.Throws<ArgumentNullException>(() => new ConstructedObjectNullCheckEliminator(graphs, null!, plan, true));
        Assert.Throws<ArgumentNullException>(() => new ConstructedObjectNullCheckEliminator(graphs, new PassThrough(), null!, true));
        Assert.Throws<ArgumentNullException>(() =>
            new ConstructedObjectNullCheckEliminator(graphs, new PassThrough(), plan, true).Rewrite(null!));
        var stacks = new TypedStackValidatorFactory(new StackTypeCompatibilityValidator());
        var graphFactory = new ControlFlowGraphBuilderFactory();
        var calls = new CalledMethodResolverFactory();
        Assert.Throws<ArgumentNullException>(() => new ConstructorReceiverProverFactory(null!, graphFactory, calls));
        Assert.Throws<ArgumentNullException>(() => new ConstructorReceiverProverFactory(stacks, null!, calls));
        Assert.Throws<ArgumentNullException>(() => new ConstructorReceiverProverFactory(stacks, graphFactory, null!));
        Assert.IsType<ConstructorReceiverProver>(new ConstructorReceiverProverFactory(stacks, graphFactory, calls).Create(
            ValidationTestData.Types(), ValidationTestData.Fields(), ValidationTestData.Methods(),
            ValidationTestData.MethodInstances(), ValidationTestData.Symbols(), enabled: true));
    }

    private static ManagedMethodBody With(ManagedMethodBody method, CilMethodBody body) =>
        new(method.Method, new PassThrough().Validate(new ControlFlowGraphBuilderFactory().Create().Build(body)));

    private static UnitFingerprint Fingerprint(ManagedMethodBody body)
    {
        var builder = new UnitFingerprintBuilder();
        builder.Add(body);
        return builder.Finish();
    }

    private sealed class FixedPlan(ImmutableHashSet<int> offsets) : IConstructedReceiverPlanner
    {
        public ImmutableHashSet<int> Plan(ValidatedControlFlowGraph controlFlow, bool thisIsNonNull)
        {
            Assert.False(thisIsNonNull);
            return offsets;
        }
    }

    private sealed class PassThrough : ITypedStackValidator
    {
        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph) => new(graph,
            graph.Blocks.ToImmutableDictionary(block => block.Index, _ => ImmutableArray<CliValueKind>.Empty),
            graph.Blocks.SelectMany(block => block.Instructions)
                .ToImmutableDictionary(instruction => instruction.Offset, _ => ImmutableArray<CliValueKind>.Empty));
    }
}
