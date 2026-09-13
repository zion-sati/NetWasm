using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Tests;

using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

public sealed class ControlFlowGraphBuilderTests
{
    [Fact]
    public void GraphBuildsConditionalJoinAndLeavesUnreachableBlockMarked()
    {
        var body = Body(
            CliValueKind.I4,
            2,
            [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(10)),
            I(3, CilOperation.Branch, new CilOperand.BranchTarget(5)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(20)),
            I(5, CilOperation.Return),
            I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(99)),
            I(7, CilOperation.Return));

        var graph = CreateGraphBuilder().Build(body);

        Assert.Equal(5, graph.Blocks.Length);
        Assert.True(graph.Successors[0].SequenceEqual([2, 1]));
        Assert.True(graph.Successors[1].SequenceEqual([3]));
        Assert.True(graph.Successors[2].SequenceEqual([3]));
        Assert.Empty(graph.Successors[3]);
        Assert.Empty(graph.Predecessors[4]);
        Assert.DoesNotContain(4, graph.ReachableBlocks);
        Assert.Equal(4, graph.GetBlockAtOffset(6).Index);
        Assert.Equal(0, graph.Entry.Index);
    }

    [Fact]
    public void BuilderRejectsMissingExceptionRegionValidator()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CilControlFlowGraphBuilder(null!));
        Assert.Throws<ArgumentNullException>(
            () => new ControlFlowGraphBuilderFactory(null!));
    }

    [Fact]
    public void DefaultBuilderFactoryComposesItsBuilderContract()
    {
        var factory = new ControlFlowGraphBuilderFactory();
        Assert.IsAssignableFrom<IControlFlowGraphBuilder>(factory.Create());
    }

    [Fact]
    public void GraphRejectsEmptyInvalidTargetAndFallingOffEnd()
    {
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(Body(
                CliValueKind.Void,
                0,
                [])),
            "method body is empty");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(Body(
                CliValueKind.Void,
                0,
                [],
                I(0, CilOperation.Branch, new CilOperand.BranchTarget(7)),
                I(1, CilOperation.Return))),
            "branch target is not an instruction boundary");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(Body(
                CliValueKind.I4,
                1,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)))),
            "control can fall off the end of the method");
    }

    [Fact]
    public void GraphBuilderRejectsMalformedExceptionFiltersDeterministically()
    {
        var valid = Body(
            CliValueKind.Void,
            2,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Throw),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(4, CilOperation.EndFilter),
            I(5, CilOperation.Pop),
            I(6, CilOperation.Leave, new CilOperand.BranchTarget(7)),
            I(7, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Filter,
                    0,
                    2,
                    5,
                    2,
                    null,
                    2),
            ],
        };

        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                ExceptionRegions =
                [valid.ExceptionRegions[0] with { FilterOffset = null }],
            }),
            "missing filter offset");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                ExceptionRegions =
                [valid.ExceptionRegions[0] with { FilterOffset = 99 }],
            }),
            "filter range is invalid");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                ExceptionRegions =
                [valid.ExceptionRegions[0] with { CatchType = TypeKey }],
            }),
            "invalid token");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                Instructions = valid.Instructions.SetItem(
                    4,
                    I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1))),
            }),
            "must end with endfilter");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                Instructions = valid.Instructions.SetItem(
                    3,
                    I(3, CilOperation.Leave, new CilOperand.BranchTarget(7))),
            }),
            "forbidden control transfer");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                Instructions = valid.Instructions.SetItem(
                    3,
                    I(3, CilOperation.Branch, new CilOperand.BranchTarget(7))),
            }),
            "branch exits an exception filter");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                Instructions = valid.Instructions.SetItem(
                    1,
                    I(1, CilOperation.Branch, new CilOperand.BranchTarget(3))),
            }),
            "branch enters an exception filter");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.EndFilter),
                I(2, CilOperation.Return))),
            "outside an exception filter");

        var filter = valid.ExceptionRegions[0];
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                ExceptionRegions = [filter with { TryLength = 0 }],
            }),
            "try range is invalid");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                ExceptionRegions = [filter with { HandlerLength = 0 }],
            }),
            "handler range is invalid");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                ExceptionRegions = [filter with { TryLength = 6 }],
            }),
            "try and handler ranges overlap");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                ExceptionRegions =
                [filter with { Kind = CilExceptionRegionKind.Catch }],
            }),
            "catch clause has invalid token or filter offset");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                ExceptionRegions =
                [filter with { Kind = CilExceptionRegionKind.Finally }],
            }),
            "finally clause has a catch token or filter offset");
        var fault = Assert.Throws<CompilerException>(() =>
            ControlFlowGraphBuilder.Build(valid with
            {
                ExceptionRegions =
                [filter with { Kind = CilExceptionRegionKind.Fault, FilterOffset = null }],
            }));
        Assert.Equal(DiagnosticCode.InvalidCil, fault.Diagnostic.Code);
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                ExceptionRegions =
                [filter with { Kind = (CilExceptionRegionKind)int.MaxValue }],
            }),
            "unknown exception region kind");
    }

    [Fact]
    public void GraphMakesExceptionalFilterEdgesExplicitAndReciprocal()
    {
        var body = Body(
            CliValueKind.Void,
            2,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Throw),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(4, CilOperation.EndFilter),
            I(5, CilOperation.Pop),
            I(6, CilOperation.Leave, new CilOperand.BranchTarget(7)),
            I(7, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Filter,
                    0,
                    2,
                    5,
                    2,
                    null,
                    2),
            ],
        };

        var graph = ControlFlowGraphBuilder.Build(body);

        Assert.True(graph.ExceptionalSuccessors[0].AsSpan().SequenceEqual([1]));
        Assert.True(graph.ExceptionalPredecessors[1].AsSpan().SequenceEqual([0]));
        Assert.All(
            graph.Blocks.Skip(1),
            block => Assert.Empty(graph.ExceptionalSuccessors[block.Index]));
    }
}
