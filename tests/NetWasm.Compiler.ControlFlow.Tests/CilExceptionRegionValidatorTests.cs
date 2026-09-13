using NetWasm.Compiler.Core;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests;

public sealed class CilExceptionRegionValidatorTests
{
    [Fact]
    public void ValidatesRegionAwareTransfersAndCleanupTerminators()
    {
        var valid = FinallyBody();

        CreateExceptionRegionValidator().Validate(
            valid,
            valid.Instructions[^1].NextOffset);

        _ = CreateGraphBuilder().Build(valid);

        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                Instructions = valid.Instructions.SetItem(
                    0,
                    I(0, CilOperation.Branch, new CilOperand.BranchTarget(2))),
            }),
            "crosses an exception-region boundary");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                Instructions = valid.Instructions.SetItem(
                    2,
                    I(2, CilOperation.Leave, new CilOperand.BranchTarget(4))),
            }),
            "leave originates in");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(valid with
            {
                Instructions = valid.Instructions.SetItem(
                    1,
                    I(1, CilOperation.Return)),
            }),
            "return exits a protected exception region");
    }

    [Fact]
    public void RejectsRegionTerminatorsOutsideTheirRequiredHandler()
    {
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(Body(
                CliValueKind.Void,
                0,
                [],
                I(0, CilOperation.EndFinally))),
            "outside a finally or fault handler");
        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(Body(
                CliValueKind.Void,
                0,
                [],
                I(0, CilOperation.Rethrow))),
            "outside a catch handler");
    }

    [Fact]
    public void FinallyMayTerminateByThrowingAReplacementException()
    {
        var body = FinallyBody();
        var throwing = body with
        {
            Instructions = body.Instructions.SetItem(
                3,
                I(3, CilOperation.Throw)),
        };

        _ = ControlFlowGraphBuilder.Build(throwing);
    }

    [Fact]
    public void LeaveMayExitANestedProtectedRegionWhileRemainingInsideFinally()
    {
        var body = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Nop),
            I(1, CilOperation.Leave, new CilOperand.BranchTarget(8)),
            I(2, CilOperation.Nop),
            I(3, CilOperation.Leave, new CilOperand.BranchTarget(7)),
            I(4, CilOperation.Pop),
            I(5, CilOperation.Leave, new CilOperand.BranchTarget(7)),
            I(6, CilOperation.Nop),
            I(7, CilOperation.EndFinally),
            I(8, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Finally,
                    0,
                    2,
                    2,
                    6,
                    null,
                    null),
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    2,
                    2,
                    4,
                    2,
                    TypeKey,
                    null),
            ],
        };

        CreateExceptionRegionValidator().Validate(
            body,
            body.Instructions[^1].NextOffset);
    }

    [Fact]
    public void ValidatesSwitchTargetsAndRejectsLeaveOutsideAnExceptionRegion()
    {
        var switchBody = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Switch, new CilOperand.SwitchTargets([1, 2])),
            I(1, CilOperation.Nop),
            I(2, CilOperation.Return));

        CreateExceptionRegionValidator().Validate(
            switchBody,
            switchBody.Instructions[^1].NextOffset);

        AssertDiagnostic(
            () => ControlFlowGraphBuilder.Build(Body(
                CliValueKind.Void,
                0,
                [],
                I(0, CilOperation.Leave, new CilOperand.BranchTarget(1)),
                I(1, CilOperation.Return))),
            "leave target does not exit to an enclosing exception region");
    }

    [Fact]
    public void ValidatesFaultCleanupAsAnExceptionZone()
    {
        var body = FinallyBody();
        var fault = body with
        {
            ExceptionRegions = [body.ExceptionRegions[0] with
            {
                Kind = CilExceptionRegionKind.Fault,
            }],
        };

        _ = CreateGraphBuilder().Build(fault);
    }

    [Fact]
    public void RejectsBranchesIntoFiltersAndUnknownExceptionKinds()
    {
        var filter = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(2)),
            I(1, CilOperation.Nop),
            I(2, CilOperation.LoadNull),
            I(3, CilOperation.EndFilter),
            I(4, CilOperation.Pop),
            I(5, CilOperation.Leave, new CilOperand.BranchTarget(6)),
            I(6, CilOperation.Return)) with
        {
            ExceptionRegions =
            [new CilExceptionRegion(
                CilExceptionRegionKind.Filter,
                0,
                2,
                4,
                2,
                null,
                2)],
        };

        AssertDiagnostic(
            () => CreateExceptionRegionValidator().Validate(
                filter,
                filter.Instructions[^1].NextOffset),
            "branch enters an exception filter");

        var branchAfterFilter = filter with
        {
            Instructions = filter.Instructions.Add(
                I(7, CilOperation.Branch, new CilOperand.BranchTarget(2))).Add(
                I(8, CilOperation.Return)),
        };
        AssertDiagnostic(
            () => CreateExceptionRegionValidator().Validate(
                branchAfterFilter,
                branchAfterFilter.Instructions[^1].NextOffset),
            "branch enters an exception filter");

        var nestedFilter = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Nop),
            I(1, CilOperation.Nop),
            I(2, CilOperation.LoadNull),
            I(3, CilOperation.EndFilter),
            I(4, CilOperation.Pop),
            I(5, CilOperation.Leave, new CilOperand.BranchTarget(9)),
            I(6, CilOperation.Pop),
            I(7, CilOperation.Leave, new CilOperand.BranchTarget(9)),
            I(8, CilOperation.Branch, new CilOperand.BranchTarget(2)),
            I(9, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    0,
                    6,
                    6,
                    2,
                    TypeKey,
                    null),
                new CilExceptionRegion(
                    CilExceptionRegionKind.Filter,
                    0,
                    2,
                    4,
                    2,
                    null,
                    2),
            ],
        };
        AssertDiagnostic(
            () => CreateExceptionRegionValidator().Validate(
                nestedFilter,
                nestedFilter.Instructions[^1].NextOffset),
            "branch enters an exception filter");

        var branchAfterFilterBoundary = filter with
        {
            Instructions = filter.Instructions.SetItem(0, I(0, CilOperation.Nop)).Add(
                I(7, CilOperation.Branch, new CilOperand.BranchTarget(0))).Add(
                I(8, CilOperation.Return)),
        };
        AssertDiagnostic(
            () => CreateExceptionRegionValidator().Validate(
                branchAfterFilterBoundary,
                branchAfterFilterBoundary.Instructions[^1].NextOffset),
            "branch crosses an exception-region boundary");

        var filterExitBeforeStart = filter with
        {
            Instructions = filter.Instructions
                .SetItem(0, I(0, CilOperation.Nop))
                .SetItem(
                2,
                I(2, CilOperation.Branch, new CilOperand.BranchTarget(0))),
        };
        AssertDiagnostic(
            () => CreateExceptionRegionValidator().Validate(
                filterExitBeforeStart,
                filterExitBeforeStart.Instructions[^1].NextOffset),
            "branch exits an exception filter");

        var filterExitAfterEnd = filterExitBeforeStart with
        {
            Instructions = filterExitBeforeStart.Instructions.SetItem(
                2,
                I(2, CilOperation.Branch, new CilOperand.BranchTarget(4))),
        };
        AssertDiagnostic(
            () => CreateExceptionRegionValidator().Validate(
                filterExitAfterEnd,
                filterExitAfterEnd.Instructions[^1].NextOffset),
            "branch exits an exception filter");

        var filterBranchWithinFilter = filterExitBeforeStart with
        {
            Instructions = filterExitBeforeStart.Instructions.SetItem(
                2,
                I(2, CilOperation.Branch, new CilOperand.BranchTarget(3))),
        };
        CreateExceptionRegionValidator().Validate(
            filterBranchWithinFilter,
            filterBranchWithinFilter.Instructions[^1].NextOffset);

        AssertDiagnostic(
            () => CreateExceptionRegionValidator().Validate(
                FinallyBody() with
                {
                    ExceptionRegions = [FinallyBody().ExceptionRegions[0] with
                    {
                        CatchType = TypeKey,
                    }],
                },
                FinallyBody().Instructions[^1].NextOffset),
            "finally clause has a");

        AssertDiagnostic(
            () => CreateExceptionRegionValidator().Validate(
                FinallyBody() with
                {
                    ExceptionRegions = [FinallyBody().ExceptionRegions[0] with
                    {
                        FilterOffset = 1,
                    }],
                },
                FinallyBody().Instructions[^1].NextOffset),
            "finally clause has a");

        var unknown = FinallyBody() with
        {
            ExceptionRegions = [FinallyBody().ExceptionRegions[0] with
            {
                Kind = (CilExceptionRegionKind)99,
            }],
        };
        AssertDiagnostic(
            () => CreateExceptionRegionValidator().Validate(
                unknown,
                unknown.Instructions[^1].NextOffset),
            "unknown exception region kind");
    }

    [Fact]
    public void RejectsBranchesBetweenSiblingExceptionZones()
    {
        var body = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(6)),
            I(1, CilOperation.Nop),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Leave, new CilOperand.BranchTarget(10)),
            I(4, CilOperation.Nop),
            I(5, CilOperation.Nop),
            I(6, CilOperation.Nop),
            I(7, CilOperation.Leave, new CilOperand.BranchTarget(10)),
            I(8, CilOperation.Pop),
            I(9, CilOperation.Leave, new CilOperand.BranchTarget(10)),
            I(10, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    0,
                    2,
                    2,
                    2,
                    TypeKey,
                    null),
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    6,
                    2,
                    8,
                    2,
                    TypeKey,
                    null),
            ],
        };

        AssertDiagnostic(
            () => CreateExceptionRegionValidator().Validate(
                body,
                body.Instructions[^1].NextOffset),
            "branch crosses an exception-region boundary");
    }

    private static CilMethodBody FinallyBody() => Body(
        CliValueKind.Void,
        0,
        [],
        I(0, CilOperation.Nop),
        I(1, CilOperation.Leave, new CilOperand.BranchTarget(4)),
        I(2, CilOperation.Nop),
        I(3, CilOperation.EndFinally),
        I(4, CilOperation.Return)) with
    {
        ExceptionRegions =
        [
            new CilExceptionRegion(
                CilExceptionRegionKind.Finally,
                0,
                2,
                2,
                2,
                null,
                null),
        ],
    };
}
