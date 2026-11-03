using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Arrays;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ArrayLoopUnrollerTests
{
    [Fact]
    public void RequiresDependenciesAndInput()
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var analysis = new RecordingAnalysis(fixture.Flow, null);
        Assert.Throws<ArgumentNullException>(() => new ArrayLoopUnroller(null!, analysis, analysis, true));
        Assert.Throws<ArgumentNullException>(() => new ArrayLoopUnroller(analysis, null!, analysis, true));
        Assert.Throws<ArgumentNullException>(() => new ArrayLoopUnroller(analysis, analysis, null!, true));
        Assert.Throws<ArgumentNullException>(() => new ArrayLoopUnroller(analysis, analysis, analysis, true).Rewrite(null!));
        Assert.Equal(0, analysis.Validations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesTheBodyWhenDisabledOrNoPlanIsSelected(bool enabled)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var original = fixture.Flow.Graph.MethodBody;
        var body = original with { ArrayBoundsProof = new(original.Instructions, original.Locals, original.LocalSignatureTypes, [7]) };
        var analysis = new RecordingAnalysis(fixture.Flow, null);

        Assert.Same(body, new ArrayLoopUnroller(analysis, analysis, analysis, enabled).Rewrite(body));
        Assert.Equal(enabled ? 1 : 0, analysis.Validations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsProofsBoundToDifferentLocalStorage(bool signature)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var original = fixture.Flow.Graph.MethodBody;
        var body = original with
        {
            ArrayBoundsProof = new(original.Instructions,
            signature ? original.Locals : [], signature ? [] : original.LocalSignatureTypes, [7])
        };
        var analysis = new RecordingAnalysis(fixture.Flow, null);

        Assert.Same(body, new ArrayLoopUnroller(analysis, analysis, analysis, true).Rewrite(body));
        Assert.Equal(0, analysis.Validations);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void KeepsScalarTailAndBindsOrderedCopiesToFreshProof(int factor)
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var original = fixture.Flow.Graph.MethodBody;
        var body = original with { ArrayBoundsProof = new(original.Instructions, original.Locals, original.LocalSignatureTypes, [7]) };
        var analysis = new RecordingAnalysis(fixture.Flow, new(0, 1, 14, [4], original.Instructions[5..13], factor));

        var result = new ArrayLoopUnroller(analysis, analysis, analysis, true).Rewrite(body);

        Assert.Equal(2, analysis.Validations);
        Assert.Equal(body.Instructions[14], result.Instructions[14]);
        Assert.Equal(new CilOperand.BranchTarget(20), result.Instructions[4].Operand);
        Assert.Equal(CilOperation.LoadArrayLength, result.Instructions[21].Operation);
        Assert.Equal(CilOperation.StoreLocal, result.Instructions[23].Operation);
        Assert.Equal(CilOperation.BranchIfGreaterThanOrEqualSigned, result.Instructions[28].Operation);
        Assert.Equal(new CilOperand.BranchTarget(14), result.Instructions[28].Operand);
        Assert.Equal(new CilOperand.ConstantI4(factor - 1), result.Instructions[26].Operand);
        Assert.Equal(new CilOperand.BranchTarget(24), result.Instructions[^1].Operand);
        Assert.Equal(body.Instructions[5..9].Select(i => i.Operation).ToArray(),
            result.Instructions[29..33].Select(i => i.Operation).ToArray());
        Assert.Equal(new CilOperand.ConstantI4(1), result.Instructions[35].Operand);
        Assert.Equal(CilOperation.Add, result.Instructions[36].Operation);
        Assert.Equal(new CilOperand.ConstantI4(factor), result.Instructions[^4].Operand);
        Assert.Single(result.Instructions[29..], instruction => instruction.Operation == CilOperation.StoreLocal &&
            instruction.Operand == new CilOperand.Index(0));
        Assert.Equal(factor + 1, result.ArrayBoundsProof!.AccessOffsets.Count);
        Assert.Equal(Enumerable.Range(0, factor).ToArray(), result.ArrayBoundsProof.IndexOffsets
            .OrderBy(pair => pair.Key).Select(pair => pair.Value.ElementOffset).ToArray());
        Assert.All(result.ArrayBoundsProof.IndexOffsets.Values, expression => Assert.Equal(0, expression.BaseLocal));
        Assert.Equal(result.Instructions, result.ArrayBoundsProof.Instructions);
        Assert.Equal(result.Locals, result.ArrayBoundsProof.Locals);
        Assert.Equal(result.LocalSignatureTypes, result.ArrayBoundsProof.LocalSignatureTypes);
        Assert.Equal(body.Locals.Length + 1, result.Locals.Length);
        Assert.NotEqual(body.Instructions, result.ArrayBoundsProof.Instructions);
        Assert.All(result.ArrayBoundsProof.AccessOffsets, offset =>
            Assert.Equal(CilOperation.LoadArrayElement, result.Instructions.Single(i => i.Offset == offset).Operation));
        Assert.Equal(3, result.MaxStack);
    }

    [Fact]
    public void DoesNotTransformWithAStaleProof()
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var body = fixture.Flow.Graph.MethodBody;
        body = body with { ArrayBoundsProof = new([], body.Locals, body.LocalSignatureTypes, [7]) };
        var analysis = new RecordingAnalysis(fixture.Flow, null);
        Assert.Same(body, new ArrayLoopUnroller(analysis, analysis, analysis, true).Rewrite(body));
        Assert.Equal(0, analysis.Validations);
    }

    // An array loop's accesses need the proof; without one its plan stays unapplied.
    [Fact]
    public void DoesNotUnrollAnArrayLoopWithoutAProof()
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var body = fixture.Flow.Graph.MethodBody;
        var analysis = new RecordingAnalysis(fixture.Flow, new(0, 1, 14, [4], body.Instructions[5..13], 4));
        Assert.Same(body, new ArrayLoopUnroller(analysis, analysis, analysis, true).Rewrite(body));
    }

    // A loop bounded by a length local, as a split span's, reads that local
    // and needs no array proof.
    [Fact]
    public void UnrollsALengthBoundLoopWithoutAProof()
    {
        var fixture = ArrayLoopInductionAnalyzerTests.Create(0, 1);
        var body = fixture.Flow.Graph.MethodBody;
        var analysis = new RecordingAnalysis(fixture.Flow, new(0, -1, 14, [4], body.Instructions[5..13], 4) { LengthLocal = 2 });

        var result = new ArrayLoopUnroller(analysis, analysis, analysis, true).Rewrite(body);

        Assert.NotSame(body, result);
        Assert.Equal(body.Locals.Length, result.Locals.Length);
        Assert.DoesNotContain(result.Instructions[body.Instructions.Length..], instruction => instruction.Operation == CilOperation.LoadArrayLength);
        Assert.Equal(new CilOperand.Index(2), result.Instructions[body.Instructions.Length + 1].Operand);
        Assert.Empty(result.ArrayBoundsProof!.AccessOffsets);
    }

    private sealed class RecordingAnalysis(ValidatedControlFlowGraph flow, ArrayLoopUnrollingPlan? plan) :
        IControlFlowGraphBuilder, ITypedStackValidator, IArrayLoopUnrollingPlanner
    {
        public int Validations { get; private set; }
        public ControlFlowGraph Build(CilMethodBody body) => flow.Graph;
        public ValidatedControlFlowGraph Validate(ControlFlowGraph graph) { Validations++; return flow; }
        public ImmutableArray<ArrayLoopUnrollingPlan> Plan(ValidatedControlFlowGraph controlFlow) => plan is null ? [] : [plan];
    }
}
