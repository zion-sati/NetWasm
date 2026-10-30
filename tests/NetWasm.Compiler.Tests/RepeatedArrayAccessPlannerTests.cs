using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class RepeatedArrayAccessPlannerTests
{
    [Theory]
    [InlineData("result = values[index] + values[index];", 1, 0)]
    [InlineData("result = values[index] + values[index + 1];", 0, 1)]
    [InlineData("result = values[index] + other[index];", 0, 0)]
    [InlineData("result = values[index]; index++; result += values[index];", 0, 1)]
    [InlineData("result = values[index]; values = other; result += values[index];", 0, 0)]
    [InlineData("result = values[index]; if (result > 0) result += values[0];", 0, 1)]
    [InlineData("if (index > 0) result = values[0]; result += values[1];", 0, 0)]
    [InlineData("result = values[index] + values[index]; Touch(ref values);", 0, 0)]
    [InlineData("result = values[index]; Touch(ref index); result += values[index];", 0, 0)]
    // The index already on the stack keeps the value it had before the store.
    [InlineData("index = index + (index = 1); result = values[index] + values[index];", 1, 0)]
    // The loop's induction proof, which covers array locals, is kept beside the repeated access.
    // The loop weighs each element by its index, so it is no sum that folds into one call.
    [InlineData("var local = values; for (var i = 0; i < local.Length; i++) result += local[i] * i; result += other[index] + other[index];", 2, 0)]
    public void ProvesAccessesThatRepeatAnEarlierCheck(string statement, int checkedAccesses, int nonNull)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("RepeatedArrayAccesses", $$"""
            using System.Runtime.CompilerServices;

            public static class EntryPoint
            {
                public static int Run(int count) => Probe(new int[count + 4], new int[count + 1], count);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Probe(int[] values, int[] other, int index)
                {
                    var result = 0;
                    {{statement}}
                    return result;
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static void Touch<T>(ref T value) => value = default!;
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: new(LoopUnrolling: ManagedLoopUnrolling.Disabled)));
        var proof = compilation.Program.Methods.Values.Single(m => m.Method.Definition.Name == "Probe").Body.ArrayBoundsProof;

        Assert.Equal(checkedAccesses, proof?.AccessOffsets.Count ?? 0);
        Assert.Equal(nonNull, proof?.NonNullOffsets.Count ?? 0);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void MethodsWithHandlersAndDisabledEliminationKeepTheirChecks(bool handler, bool disabled)
    {
        using var assets = TestAssets.Create();
        var body = handler
            ? "try { return values[index] + values[index]; } catch (System.IndexOutOfRangeException) { return values.Length; }"
            : "return values[index] + values[index];";
        var assembly = assets.CompileOptimizedSource("RepeatedArrayHandlers", $$"""
            using System.Runtime.CompilerServices;

            public static class EntryPoint
            {
                public static int Run(int count) => Probe(new int[count + 4], count);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Probe(int[] values, int index)
                {
                    {{body}}
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O3,
            OptimizationOverrides: new(ArrayBoundsCheckElimination: disabled
                ? ManagedArrayBoundsCheckElimination.Disabled
                : ManagedArrayBoundsCheckElimination.Enabled)));

        var proof = compilation.Program.Methods.Values.Single(m => m.Method.Definition.Name == "Probe").Body.ArrayBoundsProof;

        // A failed check inside a handler region does not leave the method.
        Assert.Empty(proof?.AccessOffsets ?? []);
        Assert.Empty(proof?.NonNullOffsets ?? []);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void MissingStackFactsEndWhatTheTrackedStackKnows(bool missing, int checkedAccesses)
    {
        var definition = new MethodDefinitionModel(
            new(ValidationTestData.Assembly, 0x06000001),
            new(ValidationTestData.Assembly, 0x02000001), "Reads", true,
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.ManagedReference, CliValueKind.I4), 1);
        var element = new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        // values[index]; values[index];
        (CilOperation Operation, CilOperand Operand)[] operations =
        [
            (CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.LoadArgument, new CilOperand.Index(1)),
            (CilOperation.LoadArrayElement, element), (CilOperation.Pop, new CilOperand.None()),
            (CilOperation.LoadArgument, new CilOperand.Index(0)), (CilOperation.LoadArgument, new CilOperand.Index(1)),
            (CilOperation.LoadArrayElement, element), (CilOperation.Pop, new CilOperand.None()),
            (CilOperation.Return, new CilOperand.None()),
        ];
        var body = new CilMethodBody(definition, 2, [],
            [.. operations.Select((operation, offset) =>
                new CilInstruction(offset, offset + 1, operation.Operation, operation.Operand))]);
        var graph = new ControlFlowGraphBuilderFactory().Create().Build(body);
        var reference = CliValueKind.ManagedReference;
        var stacks = new Dictionary<int, ImmutableArray<CliValueKind>>
        {
            [0] = [],
            [1] = [reference],
            [2] = [reference, CliValueKind.I4],
            [3] = [CliValueKind.I4],
            [4] = [],
            [5] = [reference],
            [6] = [reference, CliValueKind.I4],
            [7] = [CliValueKind.I4],
            [8] = [],
        };
        if (missing) stacks.Remove(5);
        var flow = new ValidatedControlFlowGraph(graph,
            graph.Blocks.ToImmutableDictionary(block => block.Index, _ => ImmutableArray<CliValueKind>.Empty),
            stacks.ToImmutableDictionary());

        var plan = new RepeatedArrayAccessPlanner(new ControlFlowDominanceAnalyzer()).Plan(flow);

        Assert.Equal(checkedAccesses, plan.Checked.Count);
    }

    [Fact]
    public void RejectsMissingDependenciesAndInput()
    {
        var graphs = new ControlFlowGraphBuilderFactory().Create();
        var dominance = new ControlFlowDominanceAnalyzer();
        Assert.Throws<ArgumentNullException>(() => new RepeatedArrayAccessPlanner(null!));
        Assert.Throws<ArgumentNullException>(() => new RepeatedArrayAccessPlanner(dominance).Plan(null!));
        Assert.Throws<ArgumentNullException>(() => new RepeatedArrayAccessEliminator(null!, new Validator(),
            new RepeatedArrayAccessPlanner(dominance), true));
        Assert.Throws<ArgumentNullException>(() => new RepeatedArrayAccessEliminator(graphs, null!,
            new RepeatedArrayAccessPlanner(dominance), true));
        Assert.Throws<ArgumentNullException>(() => new RepeatedArrayAccessEliminator(graphs, new Validator(), null!, true));
        Assert.Throws<ArgumentNullException>(() => new RepeatedArrayAccessEliminator(graphs, new Validator(),
            new RepeatedArrayAccessPlanner(dominance), true).Rewrite(null!));
    }

    private sealed class Validator : ITypedStackValidator
    {
        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph) => throw new NotSupportedException();
    }
}
