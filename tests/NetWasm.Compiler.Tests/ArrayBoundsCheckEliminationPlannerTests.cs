using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.ComponentModel;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ArrayBoundsCheckEliminationPlannerTests
{
    [Theory]
    [InlineData(CilOperation.StoreArrayElement, CliValueKind.I4)]
    [InlineData(CilOperation.StoreArrayElementReference, CliValueKind.ManagedReference)]
    public void SelectsStoresOnlyFromTheArrayAndIndexOperands(CilOperation operation, CliValueKind valueKind)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var flow = Replace(fixture.Flow, 7, new(7, 8,
            valueKind == CliValueKind.I4 ? CilOperation.LoadInt32 : CilOperation.LoadNull,
            valueKind == CliValueKind.I4 ? new CilOperand.ConstantI4(7) : new CilOperand.None()));
        flow = Replace(flow, 8, new(8, 9, operation,
            new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(valueKind))));
        flow = flow with
        {
            InstructionEntryStacks = flow.InstructionEntryStacks.SetItem(8,
                [CliValueKind.ManagedReference, CliValueKind.I4, valueKind]),
        };

        Assert.Equal([8], Create(fixture.Analyzer.Analyze(fixture.Flow), flow).Plan(flow).ToArray());
    }

    [Fact]
    public void NativeWidthIndexDoesNotReceiveAnInt32InductionProof()
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var flow = fixture.Flow with
        {
            InstructionEntryStacks = fixture.Flow.InstructionEntryStacks.SetItem(7,
                [CliValueKind.ManagedReference, CliValueKind.NativeInt]),
        };

        Assert.Empty(Create(fixture.Analyzer.Analyze(fixture.Flow), flow).Plan(flow));
    }

    [Theory]
    [InlineData(CilOperation.Call)]
    [InlineData(CilOperation.StoreField)]
    [InlineData(CilOperation.StoreObject)]
    public void LostOriginsAreNotRecoveredFromStackDepthAlone(CilOperation operation)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var flow = Replace(fixture.Flow, 7, new(7, 8, operation, new CilOperand.None()));
        flow = Replace(flow, 8, fixture.Flow.Graph.MethodBody.Instructions[7] with { Offset = 8, NextOffset = 9 });
        flow = flow with
        {
            InstructionEntryStacks = flow.InstructionEntryStacks.SetItem(8,
                [CliValueKind.ManagedReference, CliValueKind.I4]),
        };

        Assert.Empty(Create(fixture.Analyzer.Analyze(fixture.Flow), flow).Plan(flow));
    }

    [Fact]
    public void RejectsMissingDependenciesAndInput()
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var dominance = new ControlFlowDominanceAnalyzer();

        Assert.Throws<ArgumentNullException>(() => new ArrayBoundsCheckEliminationPlanner(null!, dominance));
        Assert.Throws<ArgumentNullException>(() => new ArrayBoundsCheckEliminationPlanner(fixture.Analyzer, null!));
        Assert.Throws<ArgumentNullException>(() => Create([], fixture.Flow).Plan(null!));
    }

    [Theory]
    [InlineData(CilOperation.Nop)]
    [InlineData(CilOperation.Readonly)]
    [InlineData(CilOperation.Volatile)]
    [InlineData(CilOperation.Unaligned)]
    public void StackNeutralInstructionsPreserveArrayAndIndexOrigins(CilOperation operation)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var flow = Replace(fixture.Flow, 7, new(7, 8, operation, new CilOperand.None()));
        flow = Replace(flow, 8, fixture.Flow.Graph.MethodBody.Instructions[7] with { Offset = 8, NextOffset = 9 });
        flow = flow with
        {
            InstructionEntryStacks = flow.InstructionEntryStacks.SetItem(8,
                [CliValueKind.ManagedReference, CliValueKind.I4]),
        };

        Assert.Equal([8], Create(fixture.Analyzer.Analyze(fixture.Flow), flow).Plan(flow).ToArray());
    }

    [Theory]
    [InlineData(CilOperation.Negate)]
    [InlineData(CilOperation.OnesComplement)]
    [InlineData(CilOperation.ConvertInt32)]
    public void TransformedIndexDoesNotInheritTheOriginalIndexProof(CilOperation operation)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var flow = Replace(fixture.Flow, 7, new(7, 8, operation, new CilOperand.None()));
        flow = Replace(flow, 8, fixture.Flow.Graph.MethodBody.Instructions[7] with { Offset = 8, NextOffset = 9 });
        flow = flow with
        {
            InstructionEntryStacks = flow.InstructionEntryStacks.SetItem(8,
                [CliValueKind.ManagedReference, CliValueKind.I4]),
        };

        Assert.Empty(Create(fixture.Analyzer.Analyze(fixture.Flow), flow).Plan(flow));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void ReassignmentInvalidatesOnlyOriginsOfThatLocal(int assignedLocal, bool retainsProof)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var flow = Replace(fixture.Flow, 6, new(6, 7, CilOperation.Duplicate, new CilOperand.None()));
        flow = Replace(flow, 7, new(7, 8, CilOperation.StoreLocal, new CilOperand.Index(assignedLocal)));
        flow = Replace(flow, 8, new(8, 9, CilOperation.LoadLocal, new CilOperand.Index(0)));
        flow = Replace(flow, 9, fixture.Flow.Graph.MethodBody.Instructions[7] with { Offset = 9, NextOffset = 10 });
        flow = Replace(flow, 10, new(10, 11, CilOperation.Pop, new CilOperand.None()));
        flow = Replace(flow, 11, new(11, 12, CilOperation.Nop, new CilOperand.None()));
        flow = flow with
        {
            InstructionEntryStacks = flow.InstructionEntryStacks
                .SetItem(7, [CliValueKind.ManagedReference, CliValueKind.ManagedReference])
                .SetItem(8, [CliValueKind.ManagedReference])
                .SetItem(9, [CliValueKind.ManagedReference, CliValueKind.I4])
                .SetItem(10, [CliValueKind.I4])
                .SetItem(11, []),
        };

        var accesses = Create(fixture.Analyzer.Analyze(fixture.Flow), flow).Plan(flow);

        if (retainsProof)
            Assert.Equal([9], accesses.ToArray());
        else
            Assert.Empty(accesses);
    }

    [Fact]
    public void RecognizesOrdinaryOptimizedArrayAccumulation()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ArrayRangeProofFixture", """
            public static class EntryPoint
            {
                public static int Run(int count)
                {
                    var values = new int[count];
                    var result = 0;
                    for (var index = 0; index < values.Length; index++)
                        result += values[index];
                    return result;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O3,
            // The loop stays an ordinary element loop: unrolled, folded as a
            // reduction or vectorized, it has no single load to prove.
            OptimizationOverrides: new(
                LoopUnrolling: NetWasm.Compiler.ComponentModel.Optimization.ManagedLoopUnrolling.Disabled,
                ArrayLoopIdioms: NetWasm.Compiler.ComponentModel.Optimization.ManagedArrayLoopIdioms.Disabled,
                LoopVectorization: NetWasm.Compiler.ComponentModel.Optimization.ManagedLoopVectorization.Disabled)));
        var method = compilation.Program.Methods.Values.Single(m => m.Method.Definition.Name == "Run");
        var dominance = new ControlFlowDominanceAnalyzer();
        var planner = new ArrayBoundsCheckEliminationPlanner(
            new ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), dominance), dominance);
        var accesses = planner.Plan(method.ControlFlow);
        var load = Assert.Single(method.Body.Instructions.Where(i => i.Operation == CilOperation.LoadArrayElement));
        Assert.Equal([load.Offset], accesses.ToArray());
        Assert.NotNull(method.Body.ArrayBoundsProof);
        Assert.Equal([load.Offset], method.Body.ArrayBoundsProof.AccessOffsets.ToArray());
        Assert.Equal(method.Body.Instructions, method.Body.ArrayBoundsProof.Instructions);
    }

    [Theory]
    // A guard on index + 1 proves the element at index and the one after it.
    [InlineData("for (var i = 0; i + 1 < values.Length; i++) result += values[i] * 3 + values[i + 1];", 2)]
    [InlineData("for (var i = 0; i + 2 < values.Length; i++) result += values[i + 1] + values[i + 2];", 2)]
    [InlineData("for (var i = 0; i + 1 < values.Length; i++) result += values[i + 2];", 0)]
    [InlineData("for (var i = 1; i + 1 < values.Length; i++) result += values[i - 1];", 0)]
    // Offsets that leave the int32 range lose their origin.
    [InlineData("for (var i = 0; i + 1 < values.Length; i++) result += values[i + int.MaxValue + 1];", 0)]
    [InlineData("for (var i = 0; i + 1 < values.Length; i++) result += values[i - int.MaxValue - 2];", 0)]
    // A nonzero length proves the last element until the array local changes.
    [InlineData("if (values.Length > 0) result = values[^1];", 1)]
    [InlineData("if (values.Length == 0) return 0; result = values[^1];", 1)]
    [InlineData("if (values.Length > 0) result = values[values.Length - 2];", 0)]
    [InlineData("if (values.Length > 0) { values = other; result = values[^1]; } result += values.Length;", 0)]
    [InlineData("if (values.Length > 0 || count > 3) result = values[^1];", 0)]
    [InlineData("result = count + (values.Length > 0 ? values[^1] : 0);", 1)]
    [InlineData("if (values.Length > 0) result = values[^1]; Touch(ref values);", 0)]
    [InlineData("if (values.Length > 0) result = values[^1]; Touch(ref other);", 1)]
    public void ProvesOffsetAndLastElementAccesses(string statement, int proven)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ArrayRangeShapes", $$"""
            public static class EntryPoint
            {
                public static int Run(int count)
                {
                    var values = new int[count];
                    var other = new int[count + 1];
                    var result = 0;
                    {{statement}}
                    return result + other.Length;
                }

                [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
                private static void Touch(ref int[] values) => values = new int[1];
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: new(LoopUnrolling: NetWasm.Compiler.ComponentModel.Optimization.ManagedLoopUnrolling.Disabled)));
        var method = compilation.Program.Methods.Values.Single(m => m.Method.Definition.Name == "Run");

        Assert.Equal(proven, method.Body.ArrayBoundsProof?.AccessOffsets.Count ?? 0);
    }

    [Fact]
    public void MissingStackFactsDoNotProveTheLastElement()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ArrayLastElement", """
            public static class EntryPoint
            {
                public static int Run(int count)
                {
                    var values = new int[count];
                    return values.Length > 0 ? values[^1] : 0;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O3));
        var method = compilation.Program.Methods.Values.Single(m => m.Method.Definition.Name == "Run");
        var load = Assert.Single(method.Body.Instructions.Where(i => i.Operation == CilOperation.LoadArrayElement));
        var dominance = new ControlFlowDominanceAnalyzer();
        var planner = new ArrayBoundsCheckEliminationPlanner(
            new ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), dominance), dominance);

        Assert.Equal([load.Offset], planner.Plan(method.ControlFlow).ToArray());
        Assert.Empty(planner.Plan(method.ControlFlow with
        {
            InstructionEntryStacks = method.ControlFlow.InstructionEntryStacks.Remove(load.Offset),
        }));
    }

    [Theory]
    [InlineData(CilOperation.LoadArrayElement)]
    [InlineData(CilOperation.LoadArrayElementReference)]
    [InlineData(CilOperation.LoadArrayElementAddress)]
    public void SelectsOnlyAccessWithMatchingArrayAndIndex(CilOperation operation)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var flow = Replace(fixture.Flow, 7, fixture.Flow.Graph.MethodBody.Instructions[7] with
        {
            Operation = operation,
        });
        Assert.Equal([7], Create(fixture.Analyzer.Analyze(fixture.Flow), flow).Plan(flow).ToArray());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void DifferentLocalDoesNotInheritProof(int producer)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var flow = Replace(fixture.Flow, producer,
            fixture.Flow.Graph.MethodBody.Instructions[producer] with { Operand = new CilOperand.Index(2) });
        Assert.Empty(Create(fixture.Analyzer.Analyze(fixture.Flow), flow).Plan(flow));
    }

    [Fact]
    public void MissingStackFactsDoNotEstablishAccessProvenance()
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var flow = fixture.Flow with
        {
            InstructionEntryStacks = fixture.Flow.InstructionEntryStacks.Remove(6),
        };
        Assert.Empty(Create(fixture.Analyzer.Analyze(fixture.Flow), flow).Plan(flow));
    }

    [Fact]
    public void UnprovenInductionDoesNotSelectAccesses()
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(-1, 1);
        Assert.Empty(Create([], fixture.Flow).Plan(fixture.Flow));
    }

    [Fact]
    public void AccessMustBeDominatedBySuccessfulGuardEdge()
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var proofs = fixture.Analyzer.Analyze(fixture.Flow);
        var facts = new ControlFlowDominanceAnalyzer().Analyze(fixture.Flow.Graph,
            fixture.Flow.Graph.Entry.Index, fixture.Flow.Graph.ReachableBlocks);
        facts[proofs[0].BodyEntryBlock] = [];
        var planner = Assert.IsAssignableFrom<IArrayBoundsCheckEliminationPlanner>(
            new ArrayBoundsCheckEliminationPlanner(new FrozenInduction(proofs), new FrozenDominance(facts)));
        Assert.Empty(planner.Plan(fixture.Flow));
    }

    private static IArrayBoundsCheckEliminationPlanner Create(
        ImmutableArray<ArrayLoopInduction> proofs, ValidatedControlFlowGraph flow) =>
        Assert.IsAssignableFrom<IArrayBoundsCheckEliminationPlanner>(new ArrayBoundsCheckEliminationPlanner(new FrozenInduction(proofs),
            new FrozenDominance(new ControlFlowDominanceAnalyzer().Analyze(
                flow.Graph, flow.Graph.Entry.Index, flow.Graph.ReachableBlocks))));

    private static ValidatedControlFlowGraph Replace(ValidatedControlFlowGraph flow,
        int offset, CilInstruction instruction) => flow with
        {
            Graph = new ControlFlowGraphBuilderFactory().Create().Build(flow.Graph.MethodBody with
            {
                Instructions = flow.Graph.MethodBody.Instructions.SetItem(offset, instruction),
            }),
        };

    private sealed class FrozenInduction(ImmutableArray<ArrayLoopInduction> facts) : IArrayLoopInductionAnalyzer
    {
        public ImmutableArray<ArrayLoopInduction> Analyze(ValidatedControlFlowGraph controlFlow) => facts;
    }

    private sealed class FrozenDominance(Dictionary<int, ImmutableHashSet<int>> facts) : IControlFlowDominanceAnalyzer
    {
        public Dictionary<int, ImmutableHashSet<int>> Analyze(ControlFlowGraph graph, int entry,
            ImmutableHashSet<int> blocks) => facts;
    }
}
