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
    // A dominating access to the same element of slots that keep their values
    // proves both checks; a slot stored once at entry keeps its value too.
    [InlineData("result = values[index]; if (result > 0) result += values[index];", 1, 0)]
    [InlineData("var local = other; var at = index - 1; result = local[at]; if (result >= 0) result += local[at] + local[0];", 1, 1)]
    [InlineData("result = values[index]; if (result > 0) index = 0; result += values[index];", 0, 1)]
    [InlineData("result = values[index] + values[index]; Touch(ref values);", 0, 0)]
    [InlineData("result = values[index]; Touch(ref index); result += values[index];", 0, 0)]
    // The index already on the stack keeps the value it had before the store.
    [InlineData("index = index + (index = 1); result = values[index] + values[index];", 1, 0)]
    // The loop's induction proof, which covers array locals, is kept beside the repeated access.
    // The loop scales what it has before each element, so it is no fold for a bulk call or vectors.
    [InlineData("var local = values; for (var i = 0; i < local.Length; i++) result = result * 3 + local[i]; result += other[index] + other[index];", 2, 0)]
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

    // A method every call passes the same constant is specialized after the optimizer planned its proofs (constant
    // arguments reach instance methods since 0.14), and its rewritten body plans them again, so the inner loop's Av[i]
    // keeps its proven null check: before, the body kept a proof of its old instructions, which emission ignored, and
    // every inner iteration checked Av for null again (SpectralNorm 9% slower, FannkuchRedux 5%).
    [Fact]
    public void ConstantArgumentSpecializationKeepsTheArrayProofs()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("SpecializedArrayProofs", """
            public static class EntryPoint
            {
                public static int Run(int count) => (int)new Spectral().Bench(100);
            }

            public sealed class Spectral
            {
                public double Bench(int n)
                {
                    double[] u = new double[n], v = new double[n];
                    for (int i = 0; i < n; i++) u[i] = 1;
                    MultiplyAv(n, u, v);
                    return v[0];
                }

                double A(int i, int j) => 1.0 / ((i + j) * (i + j + 1) / 2 + i + 1);

                void MultiplyAv(int n, double[] v, double[] Av)
                {
                    for (int i = 0; i < n; i++)
                    {
                        Av[i] = 0;
                        for (int j = 0; j < n; j++) Av[i] += A(i, j) * v[j];
                    }
                }
            }
            """);
        var compilation = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Optimization: FinalWasmOptimization.O3));
        var body = compilation.Program.Methods.Values.Single(m => m.Method.Definition.Name == "MultiplyAv").Body;

        Assert.Contains(body.Instructions, instruction => instruction is { Operation: CilOperation.LoadInt32, Operand: CilOperand.ConstantI4 { Value: 100 } });
        var proof = Assert.IsType<ArrayBoundsProof>(body.ArrayBoundsProof);
        Assert.True(proof.Describes(body.Instructions, body.Locals, body.LocalSignatureTypes));
        Assert.Contains(body.Instructions.Single(instruction => instruction.Operation == CilOperation.LoadArrayElementAddress).Offset,
            proof.NonNullOffsets);
    }

    // A rectangular access skips the check of each dimension whose index an
    // earlier access to the same array in the block checked against it, and its
    // null check after any earlier access; the store of `grid[j, k] = ... +
    // grid[j, k]` runs after the read it repeats. A native int index reaches the
    // access through a conversion to int32, whose result is no tracked index.
    [Theory]
    [InlineData("result = grid[i, k] + grid[j, k];", 1, 1)]
    [InlineData("result = grid[i, k] + grid[i, k];", 2, 1)]
    [InlineData("result = grid[i, k]; grid[j, k] = result + grid[j, k];", 3, 2)]
    [InlineData("result = grid[i, k] + other[i, k];", 0, 0)]
    [InlineData("result = grid[i, k] + grid[k, i];", 0, 1)]
    [InlineData("result = grid[i, k]; k++; result += grid[i, k];", 1, 1)]
    [InlineData("result = grid[i, k]; grid = other; result += grid[i, k];", 0, 0)]
    [InlineData("result = grid[i, k]; Touch(ref k); result += grid[i, k];", 1, 1)]
    [InlineData("result = grid[i, k]; if (result > 0) result += grid[i, k];", 0, 1)]
    [InlineData("result = cube[i, j, 0] + cube[i, 1, k] + cube[i, j, k];", 4, 2)]
    [InlineData("nint n = i; result = grid[n, k] + grid[n, k];", 1, 1)]
    public void ProvesRectangularDimensionsThatRepeatAnEarlierCheck(string statement, int dimensions, int nonNull)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("RectangularAccesses", $$"""
            using System.Runtime.CompilerServices;

            public static class EntryPoint
            {
                public static int Run(int count) =>
                    // Indexes no call fixes, so Probe keeps its parameters rather than being specialized for constants.
                    Probe(new int[count + 4, count + 4], new int[count + 1, count + 1], new int[3, 3, 3], count % 3, count % 2, count % 2 + 1);

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Probe(int[,] grid, int[,] other, int[,,] cube, int i, int j, int k)
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

        Assert.Equal(dimensions, proof?.CheckedDimensions.Count ?? 0);
        Assert.Equal(nonNull, proof?.NonNullOffsets.Count ?? 0);
    }

    // A field access proves its object non-null for the accesses after it in
    // the block, and for those its block dominates while the slot keeps one
    // reference; a field's own value is no slot, and `this` is argument 0.
    [Theory]
    [InlineData("result = node.A + node.B;", 1)]
    [InlineData("result = node.A + other.B;", 0)]
    [InlineData("node.A = index; result = node.B;", 1)]
    [InlineData("result = node.A; if (index > 0) result += node.B;", 1)]
    [InlineData("if (index > 0) result = node.A; result += node.B;", 0)]
    [InlineData("result = node.A; node = other; result += node.B;", 0)]
    [InlineData("result = node.A + node.B; Touch(ref node);", 0)]
    [InlineData("result = node.Next.A + node.Next.B;", 1)]
    [InlineData("result = node.Sum();", 2)]
    [InlineData("result = values[0] + node.A + values[1];", 1)]
    public void ProvesFieldAccessesOfAnObjectAlreadyAccessed(string statement, int nonNull)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("RepeatedFieldAccesses", $$"""
            using System.Runtime.CompilerServices;

            public sealed class Node
            {
                public int A, B;
                public Node Next;

                [MethodImpl(MethodImplOptions.NoInlining)]
                public int Sum() => A + B + Next.A;
            }

            public static class EntryPoint
            {
                public static int Run(int count)
                {
                    var node = new Node { A = count, Next = new Node() };
                    return Probe(node, node.Next, new int[count + 2], count);
                }

                [MethodImpl(MethodImplOptions.NoInlining)]
                private static int Probe(Node node, Node other, int[] values, int index)
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
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: FinalWasmOptimization.O3));
        var name = statement.Contains("Sum()") ? "Sum" : "Probe";
        var proof = compilation.Program.Methods.Values.Single(m => m.Method.Definition.Name == name).Body.ArrayBoundsProof;

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
