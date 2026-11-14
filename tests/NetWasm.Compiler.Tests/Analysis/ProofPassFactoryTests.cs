using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.Analysis.NullChecks;
using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis;

using static Constants.ConstantRewriteTestSupport;

// One factory per proof pass maps the optimization settings to it, for the optimizer's pipeline and for a reduced
// body's proof chain alike.
public sealed class ProofPassFactoryTests
{
    private static readonly WasmOptimizationSettings Disabled = new(NetWasm.Compiler.ComponentModel.FinalWasmOptimization.O0,
        WasmInlineAnnotationSelection.Preserve, WasmInlineAnnotationSelection.Preserve);

    private static readonly WasmOptimizationSettings Enabled = Disabled with
    {
        ArrayBoundsCheckElimination = ArrayBoundsCheckEliminationPolicy.Conservative,
        NewObjectNullCheckElimination = NewObjectNullCheckEliminationPolicy.NewObjects,
        IntegerCheckElimination = IntegerCheckEliminationPolicy.ProvenRanges,
    };

    [Fact]
    public void EachFactoryCreatesItsPass()
    {
        var graphs = new ControlFlowGraphBuilderFactory().Create();
        var stacks = Stacks();
        var dominance = new ControlFlowDominanceAnalyzer();
        var calls = new MethodFacts();

        Assert.IsType<ArrayBoundsCheckEliminator>(new ArrayBoundsCheckEliminatorFactory().Create(graphs, stacks, dominance, Enabled));
        Assert.IsType<RepeatedArrayAccessEliminator>(new RepeatedArrayAccessEliminatorFactory().Create(graphs, stacks, dominance, Enabled));
        Assert.IsType<ConstructedObjectNullCheckEliminator>(
            new ConstructedObjectNullCheckEliminatorFactory().Create(graphs, stacks, dominance, calls, Enabled));
        Assert.IsType<IntegerCheckEliminator>(new IntegerCheckEliminatorFactory().Create(graphs, stacks, calls, Enabled));

        Assert.Throws<ArgumentNullException>(() => new ArrayBoundsCheckEliminatorFactory().Create(graphs, stacks, dominance, null!));
        Assert.Throws<ArgumentNullException>(() => new RepeatedArrayAccessEliminatorFactory().Create(graphs, stacks, dominance, null!));
        Assert.Throws<ArgumentNullException>(() => new ConstructedObjectNullCheckEliminatorFactory().Create(graphs, stacks, dominance, calls, null!));
        Assert.Throws<ArgumentNullException>(() => new IntegerCheckEliminatorFactory().Create(graphs, stacks, calls, null!));
    }

    // A pass its setting disables plans nothing: the bounds pass drops the proof a body carried.
    [Fact]
    public void ADisabledPassPlansNoProof()
    {
        var body = Body(Instruction(0, CilOperation.Return)) with { ArrayBoundsProof = new([], [], [], []) };
        var bounds = new ArrayBoundsCheckEliminatorFactory().Create(
            new ControlFlowGraphBuilderFactory().Create(), Stacks(), new ControlFlowDominanceAnalyzer(), Disabled);

        Assert.Null(bounds.Rewrite(body).ArrayBoundsProof);
    }

    [Fact]
    public void TheAnalyzerFactoryRejectsMissingCollaborators()
    {
        var stacks = new TypedStackValidatorFactory(new StackTypeCompatibilityValidator());
        var graphs = new ControlFlowGraphBuilderFactory();
        var bounds = new ArrayBoundsCheckEliminatorFactory();
        var repeated = new RepeatedArrayAccessEliminatorFactory();
        var objects = new ConstructedObjectNullCheckEliminatorFactory();
        var integers = new IntegerCheckEliminatorFactory();
        var logger = NullLogger<DiagnosticReachableMethodAnalyzer>.Instance;

        Assert.Throws<ArgumentNullException>(() => new ReachableMethodAnalyzerFactory(null!, graphs, bounds, repeated, objects, integers, logger));
        Assert.Throws<ArgumentNullException>(() => new ReachableMethodAnalyzerFactory(stacks, null!, bounds, repeated, objects, integers, logger));
        Assert.Throws<ArgumentNullException>(() => new ReachableMethodAnalyzerFactory(stacks, graphs, null!, repeated, objects, integers, logger));
        Assert.Throws<ArgumentNullException>(() => new ReachableMethodAnalyzerFactory(stacks, graphs, bounds, null!, objects, integers, logger));
        Assert.Throws<ArgumentNullException>(() => new ReachableMethodAnalyzerFactory(stacks, graphs, bounds, repeated, null!, integers, logger));
        Assert.Throws<ArgumentNullException>(() => new ReachableMethodAnalyzerFactory(stacks, graphs, bounds, repeated, objects, null!, logger));
        Assert.Throws<ArgumentNullException>(() => new ReachableMethodAnalyzerFactory(stacks, graphs, bounds, repeated, objects, integers, null!));
    }

    private static ITypedStackValidator Stacks() =>
        new TypedStackValidatorFactory(new StackTypeCompatibilityValidator()).Create(new MethodFacts(), new MethodFacts(), new MethodFacts());
}
