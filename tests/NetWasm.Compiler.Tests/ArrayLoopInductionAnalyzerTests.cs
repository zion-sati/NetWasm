using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ArrayLoopInductionAnalyzerTests
{
    [Theory]
    [InlineData(CilOperation.BranchIfEqual)]
    [InlineData(CilOperation.BranchIfGreaterThanSigned)]
    [InlineData(CilOperation.BranchIfLessThanOrEqualUnsigned)]
    public void OtherComparisonRelationsDoNotEstablishTheStrictUpperBound(CilOperation comparison)
    {
        var fixture = Create(0, 1);
        var graph = fixture.Flow.Graph;
        var instruction = graph.MethodBody.Instructions[18] with { Operation = comparison };
        var body = graph.MethodBody with { Instructions = graph.MethodBody.Instructions.SetItem(18, instruction) };
        var blocks = graph.Blocks.Select(block => block with
        {
            Instructions = [.. block.Instructions.Select(item => item.Offset == 18 ? instruction : item)],
        }).ToImmutableArray();
        var flow = fixture.Flow with
        {
            Graph = new(body, blocks, graph.Successors, graph.Predecessors,
            graph.ExceptionalSuccessors, graph.ExceptionalPredecessors, graph.ReachableBlocks)
        };

        Assert.Empty(fixture.Analyzer.Analyze(flow));
    }

    [Fact]
    public void RecordsTheLookaheadOfAnOffsetGuard()
    {
        using var assets = NetWasm.TestInfrastructure.TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("ArrayLookaheadFixture", """
            public static class EntryPoint
            {
                public static int Run(int count)
                {
                    var values = new int[count];
                    var result = 0;
                    for (var index = 0; index + 3 < values.Length; index++) result += values[index + 3];
                    return result;
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: NetWasm.Compiler.ComponentModel.FinalWasmOptimization.O3));
        var method = compilation.Program.Methods.Values.Single(m => m.Method.Definition.Name == "Run");
        var dominance = new ControlFlowDominanceAnalyzer();

        var proof = Assert.Single(new ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), dominance)
            .Analyze(method.ControlFlow));

        Assert.Equal(3, proof.Lookahead);
    }

    [Fact]
    public void EmptyNaturalLoopSetHasNoInductionProofs()
    {
        var fixture = Create(0, 1);
        var analyzer = new ArrayLoopInductionAnalyzer(new FrozenLoops([]), new FrozenDominance([]));
        Assert.Empty(analyzer.Analyze(fixture.Flow));
    }

    [Fact]
    public void ExceptionRegionsPreventInductionProofs()
    {
        var fixture = Create(0, 1);
        var graph = fixture.Flow.Graph;
        var body = graph.MethodBody with
        {
            ExceptionRegions = [new(CilExceptionRegionKind.Finally, 0, 19, 19, 1, null, null)],
        };
        var flow = fixture.Flow with
        {
            Graph = new(body, graph.Blocks, graph.Successors, graph.Predecessors,
            graph.ExceptionalSuccessors, graph.ExceptionalPredecessors, graph.ReachableBlocks)
        };
        Assert.Empty(fixture.Analyzer.Analyze(flow));
    }

    // A block nothing reaches, such as the code a rewrite leaves behind its last
    // branch, can fall into the loop body without ever entering the loop.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void OnlyReachableBlocksEnterTheLoopBody(bool reachable, bool proves)
    {
        var fixture = Create(0, 1);
        var graph = fixture.Flow.Graph;
        var body = Assert.Single(fixture.Analyzer.Analyze(fixture.Flow)).BodyEntryBlock;
        var outside = reachable ? graph.Entry.Index : graph.Blocks.Length;
        var flow = fixture.Flow with
        {
            Graph = new(graph.MethodBody, graph.Blocks, graph.Successors,
                graph.Predecessors.SetItem(body, graph.Predecessors[body].Add(outside)),
                graph.ExceptionalSuccessors, graph.ExceptionalPredecessors, graph.ReachableBlocks)
        };

        Assert.Equal(proves ? 1 : 0, fixture.Analyzer.Analyze(flow).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GuardRequiresKnownEmptyEntryStack(bool missing)
    {
        var fixture = Create(0, 1);
        var guard = fixture.Flow.Graph.GetBlockAtOffset(14).Index;
        var flow = fixture.Flow with
        {
            EntryStacks = missing ? fixture.Flow.EntryStacks.Remove(guard)
            : fixture.Flow.EntryStacks.SetItem(guard, [CliValueKind.I4])
        };
        Assert.Empty(fixture.Analyzer.Analyze(flow));
    }

    [Fact]
    public void ExposingTheArrayLocalPreventsInductionProofs()
    {
        var fixture = Create(0, 1, expose: true, exposedLocal: 1);
        Assert.Empty(fixture.Analyzer.Analyze(fixture.Flow));
    }

    [Theory]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualSigned)]
    [InlineData(CilOperation.BranchIfGreaterThanOrEqualUnsigned)]
    public void ProvesTopTestedLoopWhenTheGuardBranchesToTheExit(CilOperation comparison)
    {
        var fixture = Create(0, 1);
        var instructions = fixture.Flow.Graph.MethodBody.Instructions;
        var reordered = instructions[..5].AddRange(instructions[14..19]).AddRange(instructions[5..14]).Add(instructions[19]);
        var body = fixture.Flow.Graph.MethodBody with
        {
            Instructions = [.. reordered.Select((instruction, offset) => instruction with
            {
                Offset = offset,
                NextOffset = offset + 1,
                Operation = offset == 9 ? comparison : instruction.Operation,
                Operand = offset == 9 ? new CilOperand.BranchTarget(19)
                    : instruction.Operand is CilOperand.BranchTarget ? new CilOperand.BranchTarget(5) : instruction.Operand,
            })],
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var dominance = new ControlFlowDominanceAnalyzer();
        var flow = new ValidatedControlFlowGraph(graph,
            graph.Blocks.ToImmutableDictionary(block => block.Index, _ => ImmutableArray<CliValueKind>.Empty), []);
        var analyzer = new ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), dominance);

        var proof = Assert.Single(analyzer.Analyze(flow));

        Assert.Equal(5, graph.GetBlock(proof.GuardBlock).StartOffset);
        Assert.Equal(10, graph.GetBlock(proof.BodyEntryBlock).StartOffset);
        Assert.Equal(17, proof.IncrementOffset);
        Assert.Equal(0, proof.IndexLocal);
        Assert.Equal(1, proof.ArrayLocal);
    }

    [Fact]
    public void RejectsMissingAnalysisDependencies()
    {
        var dominance = new ControlFlowDominanceAnalyzer();
        Assert.Throws<ArgumentNullException>(() => new ArrayLoopInductionAnalyzer(null!, dominance));
        Assert.Throws<ArgumentNullException>(() => new ArrayLoopInductionAnalyzer(new ControlFlowNaturalLoopAnalyzer(dominance), null!));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(7, false)]
    [InlineData(int.MaxValue, false)]
    [InlineData(0, true)]
    public void ProvesNonnegativeUnitInductionUnderStableArrayLength(int initial, bool useUnsignedComparison)
    {
        var fixture = Create(initial, 1, useUnsignedComparison);
        var proof = Assert.Single(fixture.Analyzer.Analyze(fixture.Flow));
        Assert.Equal(0, proof.IndexLocal);
        Assert.Equal(1, proof.ArrayLocal);
        Assert.Equal(12, proof.IncrementOffset);
        Assert.Equal(14, fixture.Flow.Graph.GetBlock(proof.GuardBlock).StartOffset);
        Assert.Equal(5, fixture.Flow.Graph.GetBlock(proof.BodyEntryBlock).StartOffset);
        Assert.Equal(2, proof.LoopBlocks.Count);
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(int.MinValue, 1)]
    [InlineData(0, 2)]
    [InlineData(0, -1)]
    public void DoesNotProveNegativeInitializationOrOtherUpdates(int initial, int step)
    {
        var fixture = Create(initial, step);
        Assert.Empty(fixture.Analyzer.Analyze(fixture.Flow));
    }

    [Fact]
    public void AddressExposureAnywherePreventsProof()
    {
        var fixture = Create(0, 1, expose: true);
        Assert.Empty(fixture.Analyzer.Analyze(fixture.Flow));
    }

    [Fact]
    public void RejectsNullInput()
    {
        var fixture = Create(0, 1);
        Assert.Throws<ArgumentNullException>(() => fixture.Analyzer.Analyze(null!));
    }

    internal static (IArrayLoopInductionAnalyzer Analyzer, ValidatedControlFlowGraph Flow) Create(
        int initial, int step, bool unsigned = false, bool expose = false, int exposedLocal = 0)
    {
        var definition = new MethodDefinitionModel(
            new(ValidationTestData.Assembly, 0x06000001),
            new(ValidationTestData.Assembly, 0x02000001), "Loop", true,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.ManagedReference), 1);
        var operations = new List<(CilOperation Operation, CilOperand Operand)>
        {
            (CilOperation.LoadArgument, new CilOperand.Index(0)),
            (CilOperation.StoreLocal, new CilOperand.Index(1)),
            (CilOperation.LoadInt32, new CilOperand.ConstantI4(initial)),
            (CilOperation.StoreLocal, new CilOperand.Index(0)),
            (CilOperation.Branch, new CilOperand.BranchTarget(14)),
            (CilOperation.LoadLocal, new CilOperand.Index(1)),
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.I4))),
            (CilOperation.Pop, new CilOperand.None()),
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.LoadInt32, new CilOperand.ConstantI4(step)),
            (CilOperation.Add, new CilOperand.None()),
            (CilOperation.StoreLocal, new CilOperand.Index(0)),
            (CilOperation.Branch, new CilOperand.BranchTarget(14)),
            (CilOperation.LoadLocal, new CilOperand.Index(0)),
            (CilOperation.LoadLocal, new CilOperand.Index(1)),
            (CilOperation.LoadArrayLength, new CilOperand.None()),
            (CilOperation.ConvertInt32, new CilOperand.None()),
            (unsigned ? CilOperation.BranchIfLessThanUnsigned : CilOperation.BranchIfLessThanSigned,
                new CilOperand.BranchTarget(5)),
            (CilOperation.Return, new CilOperand.None()),
        };
        if (expose)
        {
            operations.Add((CilOperation.LoadLocalAddress, new CilOperand.Index(exposedLocal)));
            operations.Add((CilOperation.Pop, new CilOperand.None()));
            operations.Add((CilOperation.Return, new CilOperand.None()));
        }
        var body = new CilMethodBody(definition, 2, [CliValueKind.I4, CliValueKind.ManagedReference],
            [.. operations.Select((op, offset) => new CilInstruction(offset, offset + 1, op.Operation, op.Operand))])
        {
            LocalSignatureTypes = [CliTypeIdentity.FromStackKind(CliValueKind.I4),
                CliTypeIdentity.SzArray(CliTypeIdentity.FromStackKind(CliValueKind.I4))],
        };
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var dominance = new ControlFlowDominanceAnalyzer();
        var loops = new ControlFlowNaturalLoopAnalyzer(dominance)
            .Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        var facts = dominance.Analyze(graph, graph.Entry.Index, graph.ReachableBlocks);
        var stacks = new Dictionary<int, ImmutableArray<CliValueKind>>
        {
            [5] = [],
            [6] = [CliValueKind.ManagedReference],
            [7] = [CliValueKind.ManagedReference, CliValueKind.I4],
            [8] = [CliValueKind.I4],
            [9] = [],
            [10] = [CliValueKind.I4],
            [11] = [CliValueKind.I4, CliValueKind.I4],
            [12] = [CliValueKind.I4],
            [13] = [],
        }.ToImmutableDictionary();
        var validated = new ValidatedControlFlowGraph(graph,
            graph.Blocks.ToImmutableDictionary(b => b.Index, _ => ImmutableArray<CliValueKind>.Empty), stacks);
        return (new ArrayLoopInductionAnalyzer(new FrozenLoops(loops), new FrozenDominance(facts)), validated);
    }

    private sealed class FrozenLoops(ImmutableArray<ImmutableHashSet<int>> facts) : IControlFlowNaturalLoopAnalyzer
    {
        public ImmutableArray<ImmutableHashSet<int>> Analyze(ControlFlowGraph graph, int entry,
            ImmutableHashSet<int> blocks) => facts;
    }

    private sealed class FrozenDominance(Dictionary<int, ImmutableHashSet<int>> facts) : IControlFlowDominanceAnalyzer
    {
        public Dictionary<int, ImmutableHashSet<int>> Analyze(ControlFlowGraph graph, int entry,
            ImmutableHashSet<int> blocks) => facts;
    }
}
