using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using New = NetWasm.Compiler.ControlFlow.Structured;
using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

namespace NetWasm.Compiler.ControlFlow.Tests.Structured;

public sealed class StructuredMethodValidatorTests
{
    [Fact]
    public void ValidateAcceptsProjectedStructuredMethod()
    {
        var method = SimpleMethod();
        ((New.IStructuredMethodValidatorFactory)new New.StructuredMethodValidatorFactory())
            .Create()
            .Validate(method);
    }

    [Fact]
    public void OwnershipContractRejectsMissingAndDuplicateOwners()
    {
        var method = SimpleMethod();
        var empty = method with { Body = New.StructuredSequence.Empty };
        var owner = Assert.IsType<New.StructuredCode>(Assert.Single(method.Body.Regions));
        var duplicate = method with { Body = new([owner, owner]) };

        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredBlockOwnershipValidator)new New.StructuredBlockOwnershipValidator()).Validate(empty));
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredBlockOwnershipValidator)new New.StructuredBlockOwnershipValidator()).Validate(duplicate));
        var group = RootedGroup(method) with
        {
            ContinuationDispatcher = new New.StructuredDispatcher(null, [], []),
        };
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredBlockOwnershipValidator)new New.StructuredBlockOwnershipValidator()).Validate(method with
            {
                Body = new([
                    new New.StructuredExceptionRegion(group.Id, null),
                    new New.StructuredExceptionRegion(group.Id, null),
                ]),
                ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId,
                    New.StructuredExceptionGroup>.Empty.Add(group.Id, group),
            }));
        Assert.Throws<ArgumentNullException>(() =>
            ((New.IStructuredBlockOwnershipValidator)new New.StructuredBlockOwnershipValidator()).Validate(null!));
    }

    [Fact]
    public void TargetContractRejectsUnknownBlocksAndUnscopedMarkers()
    {
        var method = SimpleMethod();
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredTargetValidator)new New.StructuredTargetValidator()).Validate(
                method with { EntryBlock = new(99) }));
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredTargetValidator)new New.StructuredTargetValidator()).Validate(method with
            {
                Body = new([new New.StructuredCode(new(new(99), New.StructuredBlockRole.Owner))]),
            }));
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredTargetValidator)new New.StructuredTargetValidator()).Validate(method with
            {
                Body = new([new New.StructuredLoopBreak()]),
            }));
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredTargetValidator)new New.StructuredTargetValidator()).Validate(method with
            {
                Body = new([new New.StructuredLoopContinue()]),
            }));
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredTargetValidator)new New.StructuredTargetValidator()).Validate(method with
            {
                Body = new([new New.StructuredDispatcherContinue(new(0))]),
            }));
        Assert.Throws<ArgumentNullException>(() =>
            ((New.IStructuredTargetValidator)new New.StructuredTargetValidator()).Validate(null!));
    }

    [Fact]
    public void ExceptionContractRejectsUnknownAndUnreachableGroups()
    {
        var method = SimpleMethod();
        var group = RootedGroup(method);

        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredExceptionValidator)new New.StructuredExceptionValidator()).Validate(method with
            {
                ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId, New.StructuredExceptionGroup>.Empty
                .Add(group.Id, group),
            }));
        Assert.Throws<ArgumentNullException>(() =>
            ((New.IStructuredExceptionValidator)new New.StructuredExceptionValidator()).Validate(null!));
    }

    [Fact]
    public void ExceptionContractRejectsInvalidTopLevelGroupFacts()
    {
        var method = SimpleMethod();
        var group = RootedGroup(method);
        var rooted = method with
        {
            Body = new([new New.StructuredExceptionRegion(group.Id, null)]),
            TopLevelExceptionGroups = [group.Id],
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId, New.StructuredExceptionGroup>.Empty
                .Add(group.Id, group),
        };

        Assert.Throws<InvalidOperationException>(() => ValidateExceptions(rooted with
        {
            TopLevelExceptionGroups = [group.Id, group.Id],
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateExceptions(rooted with
        {
            TopLevelExceptionGroups = [new(9)],
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateExceptions(rooted with
        {
            ExceptionGroups = rooted.ExceptionGroups.SetItem(group.Id, group with { Parent = new(9) }),
        }));
    }

    [Fact]
    public void StackContractRejectsMissingAndOversizedFacts()
    {
        var method = SimpleMethod();
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredStackContractValidator)new New.StructuredStackContractValidator()).Validate(method with
            {
                InstructionEntryStacks = ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
            }));
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredStackContractValidator)new New.StructuredStackContractValidator()).Validate(method with
            {
                Header = method.Header with { MaxStack = -1 },
            }));
        Assert.Throws<ArgumentNullException>(() =>
            ((New.IStructuredStackContractValidator)new New.StructuredStackContractValidator()).Validate(null!));
    }

    [Fact]
    public void PublicValidatorFactoryCreatesCapability()
    {
        Assert.IsAssignableFrom<New.IStructuredMethodValidator>(
            ((New.IStructuredMethodValidatorFactory)new New.StructuredMethodValidatorFactory()).Create());
    }

    [Fact]
    public void InstructionContractValidatesEveryExitAgainstOrderedSourceFacts()
    {
#pragma warning disable CA1859 // Contract test deliberately dispatches through the capability interface.
        New.IStructuredInstructionContractValidator validator =
            new New.StructuredInstructionContractValidator();
#pragma warning restore CA1859
        var method = SimpleMethod();
        var target = method.EntryBlock;
        var branchOperand = new CilOperand.BranchTarget(0);
        var condition = new New.StructuredCondition(
            0,
            CilOperation.BranchIfTrue,
            0,
            CliValueKind.I4,
            null);

        validator.Validate(method);
        validator.Validate(WithSourceExit(
            method,
            I(0, CilOperation.Nop),
            new New.StructuredFallthroughExit(null)));
        validator.Validate(WithSourceExit(
            method,
            I(0, CilOperation.Branch, branchOperand),
            new New.StructuredBranchExit(0, target)));
        validator.Validate(WithSourceExit(
            method,
            I(0, CilOperation.Leave, branchOperand),
            new New.StructuredLeaveExit(0, target)));
        validator.Validate(WithSourceExit(
            method,
            I(0, CilOperation.BranchIfTrue, branchOperand),
            new New.StructuredConditionalExit(condition, target, target)));
        Assert.Throws<InvalidOperationException>(() =>
            validator.Validate(WithSingleExit(method, new UnsupportedBlockExit())));

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(method with
        {
            Header = method.Header with
            {
                Instructions = [I(1, CilOperation.Nop), I(0, CilOperation.Return)],
            },
        }));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(method with
        {
            Blocks = method.Blocks.SetItem(
                target,
                method.Blocks[target] with { EndOffset = 0 }),
        }));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(WithSourceExit(
            method,
            I(0, CilOperation.Nop),
            new New.StructuredBranchExit(0, target))));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(WithSourceExit(
            method,
            I(0, CilOperation.Branch, new CilOperand.BranchTarget(9)),
            new New.StructuredBranchExit(0, target))));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(WithSourceExit(
            method,
            I(0, CilOperation.BranchIfFalse, branchOperand),
            new New.StructuredConditionalExit(condition, target, target))));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(method with
        {
            Header = method.Header with { Instructions = [I(1, CilOperation.Return)] },
        }));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(method with
        {
            Blocks = method.Blocks.SetItem(
                target,
                method.Blocks[target] with
                {
                    Exit = new New.StructuredTerminalExit(I(0, CilOperation.Throw)),
                }),
        }));
    }

    [Fact]
    public void ValidatorFacadeRequiresAndInvokesEveryCapability()
    {
        var calls = new List<string>();
        var instructions = new InstructionProbe(calls);
        var ownership = new OwnershipProbe(calls);
        var targets = new TargetProbe(calls);
        var exceptions = new ExceptionProbe(calls);
        var stacks = new StackProbe(calls);

        var validator = new New.StructuredMethodValidator(
            instructions,
            ownership,
            targets,
            exceptions,
            stacks);
        ((New.IStructuredMethodValidator)validator).Validate(SimpleMethod());

        Assert.Equal(["instructions", "ownership", "targets", "exceptions", "stacks"], calls);
        Assert.Throws<ArgumentNullException>(() => ((New.IStructuredMethodValidator)validator).Validate(null!));
        Assert.Throws<ArgumentNullException>(() => new New.StructuredMethodValidator(null!, ownership, targets, exceptions, stacks));
        Assert.Throws<ArgumentNullException>(() => new New.StructuredMethodValidator(instructions, null!, targets, exceptions, stacks));
        Assert.Throws<ArgumentNullException>(() => new New.StructuredMethodValidator(instructions, ownership, null!, exceptions, stacks));
        Assert.Throws<ArgumentNullException>(() => new New.StructuredMethodValidator(instructions, ownership, targets, null!, stacks));
        Assert.Throws<ArgumentNullException>(() => new New.StructuredMethodValidator(instructions, ownership, targets, exceptions, null!));
    }

    [Fact]
    public void TargetContractRejectsInvalidExitFacts()
    {
        var method = SimpleMethod();
        ValidateTargets(WithSingleExit(method, new UnsupportedBlockExit()));
        var condition = new New.StructuredCondition(0, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null);
        var unknown = new New.StructuredBlockId(99);
        New.StructuredBlockExit[] exits =
        [
            new New.StructuredFallthroughExit(unknown),
            new New.StructuredBranchExit(0, unknown),
            new New.StructuredConditionalExit(condition, unknown, method.EntryBlock),
            new New.StructuredConditionalExit(condition, method.EntryBlock, unknown),
            new New.StructuredLeaveExit(0, unknown),
            new New.StructuredTerminalExit(I(0, CilOperation.Nop)),
        ];

        foreach (var exit in exits)
            Assert.Throws<InvalidOperationException>(() => ValidateTargets(WithSingleExit(method, exit)));

        var block = Assert.Single(method.Blocks.Values);
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(method with
        {
            EntryBlock = new(1),
            Blocks = ImmutableDictionary<New.StructuredBlockId, New.StructuredBlockDefinition>.Empty
                .Add(new(1), block),
        }));
    }

    [Fact]
    public void TargetContractRejectsInvalidStructuredRegions()
    {
        var method = SimpleMethod();
        var occurrence = new New.StructuredBlockOccurrence(method.EntryBlock, New.StructuredBlockRole.Owner);
        var unknown = new New.StructuredBlockId(99);
        var condition = new New.StructuredCondition(0, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null);
        var conditionalBlock = Assert.Single(method.Blocks.Values) with
        {
            Exit = new New.StructuredConditionalExit(condition, method.EntryBlock, method.EntryBlock),
        };

        Assert.Throws<InvalidOperationException>(() => ValidateTargets(method with
        {
            Blocks = method.Blocks.SetItem(method.EntryBlock, conditionalBlock),
            Body = new([new New.StructuredCode(occurrence)]),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(method with
        {
            Body = new([new New.StructuredIf(occurrence, New.StructuredSequence.Empty, New.StructuredSequence.Empty)]),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(method with
        {
            Body = new([new New.StructuredCode(occurrence with { LeaveContinuation = new(0) })]),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(method with
        {
            Body = new([new New.StructuredDispatcher(
                unknown,
                [new(occurrence, null, null)],
                [])]),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(method with
        {
            Body = new([new New.StructuredDispatcher(
                method.EntryBlock,
                [new(occurrence, unknown, null)],
                [])]),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(method with
        {
            Body = new([new New.StructuredExceptionRegion(new(9), null)]),
        }));
    }

    [Fact]
    public void TargetContractValidatesNestedMarkersDispatchersAndLeaveRoles()
    {
        var method = SimpleMethod();
        var block = Assert.Single(method.Blocks.Values);
        var condition = new New.StructuredCondition(0, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null);
        var conditionBlock = block with
        {
            Exit = new New.StructuredConditionalExit(condition, block.Id, block.Id),
        };
        var conditionOccurrence = new New.StructuredBlockOccurrence(block.Id, New.StructuredBlockRole.Owner);
        var markerBody = new New.StructuredSequence([new New.StructuredLoopBreak(), new New.StructuredLoopContinue()]);
        var loops = method with
        {
            Blocks = method.Blocks.SetItem(block.Id, conditionBlock),
            Body = new New.StructuredSequence(
            [
                new New.StructuredLoop(
                    conditionOccurrence,
                    true,
                    markerBody,
                    New.StructuredSequence.Empty,
                    New.StructuredSequence.Empty),
                new New.StructuredPostTestLoop(
                    markerBody,
                    conditionOccurrence with { Role = New.StructuredBlockRole.ExecutingReplica },
                    false,
                    New.StructuredSequence.Empty,
                    New.StructuredSequence.Empty),
            ]),
        };
        ValidateTargets(loops);

        var dispatcher = method with
        {
            Body = new New.StructuredSequence(
            [
                new New.StructuredDispatcher(
                    block.Id,
                    [new(conditionOccurrence, block.Id, block.Id)],
                    [new(block.Id, new([new New.StructuredDispatcherContinue(block.Id)]))]),
            ]),
        };
        ValidateTargets(dispatcher);

        var continuation = new New.StructuredExceptionContinuation(
            new(0),
            block.StartOffset,
            block.Id,
            New.StructuredSequence.Empty);
        var group = RootedGroup(method) with { NormalContinuations = [continuation] };
        var leaveBlock = block with { Exit = new New.StructuredLeaveExit(0, block.Id) };
        New.StructuredMethod WithLeave(New.StructuredBlockOccurrence leaveOccurrence) => method with
        {
            Blocks = method.Blocks.SetItem(block.Id, leaveBlock),
            Body = new([new New.StructuredExceptionRegion(group.Id, null)]),
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId, New.StructuredExceptionGroup>.Empty
                .Add(group.Id, group with
                {
                    ProtectedParts = [new New.StructuredExceptionCode(new([new New.StructuredCode(leaveOccurrence)]))],
                }),
        };

        ValidateTargets(WithLeave(new(block.Id, New.StructuredBlockRole.Owner, continuation.Id)));
        ValidateTargets(method with
        {
            Blocks = method.Blocks.SetItem(block.Id, leaveBlock),
            Body = new([new New.StructuredCode(new(block.Id, New.StructuredBlockRole.RoutingReplica))]),
        });
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(method with
        {
            Blocks = method.Blocks.SetItem(block.Id, leaveBlock),
            Body = new([new New.StructuredCode(new(
                block.Id,
                New.StructuredBlockRole.RoutingReplica,
                continuation.Id))]),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(method with
        {
            Blocks = method.Blocks.SetItem(block.Id, leaveBlock),
            Body = new([new New.StructuredCode(new(block.Id, New.StructuredBlockRole.Owner))]),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(WithLeave(new(
            block.Id,
            New.StructuredBlockRole.Owner,
            new(9)))));
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(WithLeave(new(
            block.Id,
            New.StructuredBlockRole.Owner,
            continuation.Id)) with
        {
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId, New.StructuredExceptionGroup>.Empty
                .Add(group.Id, group with
                {
                    NormalContinuations = [continuation with { Target = new(9) }],
                    ProtectedParts = [new New.StructuredExceptionCode(new([new New.StructuredCode(new(
                        block.Id,
                        New.StructuredBlockRole.Owner,
                        continuation.Id))]))],
                }),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(method with
        {
            Body = new([new New.StructuredExceptionRegion(group.Id, new(9))]),
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId, New.StructuredExceptionGroup>.Empty
                .Add(group.Id, group),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(method with
        {
            Body = new([new New.StructuredExceptionRegion(group.Id, null)]),
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId, New.StructuredExceptionGroup>.Empty
                .Add(group.Id, group with
                {
                    ProtectedParts = [new New.StructuredNestedExceptionGroup(new(9))],
                }),
        }));
    }

    [Fact]
    public void TargetContractValidatesExceptionContinuationsPerOccurrenceContext()
    {
        var method = SimpleMethod();
        var block = Assert.Single(method.Blocks.Values);
        var condition = new New.StructuredCondition(
            0,
            CilOperation.BranchIfTrue,
            0,
            CliValueKind.I4,
            null);
        var conditionalBlock = block with
        {
            Exit = new New.StructuredConditionalExit(condition, block.Id, block.Id),
        };
        New.StructuredMethod Candidate(New.StructuredRegion marker)
        {
            var continuation = new New.StructuredExceptionContinuation(
                new(0),
                block.StartOffset,
                block.Id,
                new([marker]));
            var group = RootedGroup(method) with { NormalContinuations = [continuation] };
            var exception = new New.StructuredExceptionRegion(group.Id, continuation.Id);
            var inLoop = new New.StructuredLoop(
                new(block.Id, New.StructuredBlockRole.ExecutingReplica),
                true,
                new([new New.StructuredExceptionRegion(group.Id, null)]),
                New.StructuredSequence.Empty,
                New.StructuredSequence.Empty);
            return method with
            {
                Blocks = method.Blocks.SetItem(block.Id, conditionalBlock),
                Body = new([exception, inLoop]),
                ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId, New.StructuredExceptionGroup>.Empty
                    .Add(group.Id, group),
            };
        }

        foreach (var candidate in new[]
                 {
                     Candidate(new New.StructuredLoopContinue()),
                     Candidate(new New.StructuredLoopBreak()),
                 })
        {
            ValidateTargets(candidate);
            var group = Assert.Single(candidate.ExceptionGroups.Values);
            Assert.Throws<InvalidOperationException>(() => ValidateTargets(candidate with
            {
                Body = new([new New.StructuredExceptionRegion(group.Id, null)]),
            }));
        }
    }

    [Fact]
    public void TargetContractRecognizesRoutedAndDispatchedExceptionContinuations()
    {
        var method = SimpleMethod();
        var block = Assert.Single(method.Blocks.Values);
        var parentContinuation = new New.StructuredExceptionContinuation(
            new(0),
            block.StartOffset,
            block.Id,
            New.StructuredSequence.Empty);
        var parent = RootedGroup(method) with
        {
            NormalContinuations = [parentContinuation],
        };
        var childContinuation = parentContinuation with
        {
            Body = new([new New.StructuredLoopContinue()]),
        };
        var child = RootedGroup(method) with
        {
            Id = new(1),
            Parent = parent.Id,
            NormalContinuations = [childContinuation],
        };
        parent = parent with
        {
            ProtectedParts = [new New.StructuredNestedExceptionGroup(child.Id)],
        };
        var routed = method with
        {
            Body = new([new New.StructuredExceptionRegion(parent.Id, parentContinuation.Id)]),
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId, New.StructuredExceptionGroup>.Empty
                .Add(parent.Id, parent)
                .Add(child.Id, child),
        };
        ValidateTargets(routed);

        var dispatched = parent with
        {
            ProtectedParts = [new New.StructuredExceptionCode(New.StructuredSequence.Empty)],
            NormalContinuations = [childContinuation],
            ContinuationDispatcher = new(null, [], []),
        };
        ValidateTargets(method with
        {
            Body = new([new New.StructuredExceptionRegion(dispatched.Id, null)]),
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId, New.StructuredExceptionGroup>.Empty
                .Add(dispatched.Id, dispatched),
        });

        var dispatcherRoutedRegion = new New.StructuredExceptionRegion(child.Id, null)
        {
            DispatcherContinuations = [childContinuation.Id],
        };
        var dispatcherRouted = method with
        {
            Body = new(
            [
                new New.StructuredDispatcher(
                    null,
                    [],
                    [new(block.Id, new([dispatcherRoutedRegion]))]),
            ]),
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId, New.StructuredExceptionGroup>
                .Empty.Add(child.Id, child),
        };
        ValidateTargets(dispatcherRouted);
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(dispatcherRouted with
        {
            Body = new([dispatcherRoutedRegion]),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateTargets(dispatcherRouted with
        {
            Body = new(
            [
                new New.StructuredDispatcher(
                    null,
                    [],
                    [new(block.Id, new([dispatcherRoutedRegion with
                    {
                        DispatcherContinuations = [new(99)],
                    }]))]),
            ]),
        }));
    }

    [Fact]
    public void ExceptionContractRejectsEveryInconsistentGroupFact()
    {
        var method = SimpleMethod();
        var group = RootedGroup(method);
        var block = Assert.Single(method.Blocks.Values);
        var catchType = new EntityKey(default, 1);
        var validClause = new New.StructuredExceptionClause(
            CilExceptionRegionKind.Catch,
            block.StartOffset,
            1,
            catchType,
            null,
            New.StructuredSequence.Empty,
            null)
        {
            HandlerBlock = block.Id,
        };
        var validContinuation = new New.StructuredExceptionContinuation(
            new(0),
            Assert.Single(method.Blocks.Values).StartOffset,
            method.EntryBlock,
            New.StructuredSequence.Empty);
        var validFilter = validClause with
        {
            Kind = CilExceptionRegionKind.Filter,
            CatchType = null,
            FilterOffset = block.StartOffset,
            FilterBody = New.StructuredSequence.Empty,
            FilterBlock = block.Id,
        };
        ValidateExceptions(method with
        {
            Body = new([new New.StructuredExceptionRegion(group.Id, null)]),
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId,
                New.StructuredExceptionGroup>.Empty.Add(
                    group.Id,
                    group with { Clauses = [validFilter] }),
        });

        AssertInvalidGroup(method, group with { Id = new(1) });
        AssertInvalidGroup(method, group with { TryOffset = -1 });
        AssertInvalidGroup(method, group with { TryLength = 0 });
        AssertInvalidGroup(method, group with { ProtectedBlocks = [] });
        AssertInvalidGroup(method, group with { ProtectedBlocks = [new(99)] });
        AssertInvalidGroup(method, group with { NormalContinuations = [validContinuation, validContinuation] });
        AssertInvalidGroup(method, group with
        {
            NormalContinuations = [validContinuation with { Target = new(99) }],
        });
        AssertInvalidGroup(method, group with
        {
            NormalContinuations = [validContinuation with { TargetOffset = 99 }],
        });
        AssertInvalidGroup(method, group with { ContinuationJoinBlock = new(99) });
        AssertInvalidGroup(method, group with { Clauses = [validClause with { HandlerOffset = -1 }] });
        AssertInvalidGroup(method, group with { Clauses = [validClause with { HandlerLength = 0 }] });
        AssertInvalidGroup(method, group with { Clauses = [validClause with { HandlerBlock = new(99) }] });
        AssertInvalidGroup(method, group with { Clauses = [validClause with { CatchType = null }] });
        AssertInvalidGroup(method, group with
        {
            Clauses = [validClause with { Kind = CilExceptionRegionKind.Finally }],
        });
        AssertInvalidGroup(method, group with
        {
            Clauses = [validClause with
            {
                Kind = CilExceptionRegionKind.Filter,
                CatchType = null,
            }],
        });
        AssertInvalidGroup(method, group with
        {
            Clauses = [validClause with
            {
                Kind = CilExceptionRegionKind.Filter,
                CatchType = null,
                FilterOffset = 0,
                FilterBlock = method.EntryBlock,
            }],
        });
        AssertInvalidGroup(method, group with
        {
            Clauses = [validClause with { FilterOffset = 0 }],
        });
        AssertInvalidGroup(method, group with
        {
            Clauses = [validClause with { FilterBody = New.StructuredSequence.Empty }],
        });
        AssertInvalidGroup(method, group with
        {
            Clauses = [validFilter with { FilterBlock = null }],
        });
        AssertInvalidGroup(method, group with
        {
            Clauses = [validFilter with { FilterBlock = new(99) }],
        });
        AssertInvalidGroup(method, group with
        {
            Clauses = [validFilter with { FilterOffset = block.StartOffset + 1 }],
        });
        AssertInvalidGroup(method, group with
        {
            Clauses = [validClause with { FilterBlock = block.Id }],
        });
        AssertInvalidGroup(method, group with { Parent = new(9) });
        AssertInvalidGroup(method, group with { ProtectedParts = [new New.StructuredNestedExceptionGroup(new(9))] });
    }

    [Fact]
    public void ExceptionContractDiscoversGroupsAcrossEveryStructuredContainer()
    {
        var method = SimpleMethod();
        var block = Assert.Single(method.Blocks.Values);
        var occurrence = new New.StructuredBlockOccurrence(block.Id, New.StructuredBlockRole.ExecutingReplica);
        New.StructuredSequence Region(int id) => new([new New.StructuredExceptionRegion(new(id), null)]);
        var groups = Enumerable.Range(0, 12).ToImmutableDictionary(
            id => new New.StructuredExceptionGroupId(id),
            id => RootedGroup(method) with { Id = new(id) });
        groups = groups.SetItem(new(11), groups[new(11)] with { Parent = new(9) });
        var continuation = new New.StructuredExceptionContinuation(
            new(0),
            block.StartOffset,
            block.Id,
            Region(10));
        var filterClause = new New.StructuredExceptionClause(
            CilExceptionRegionKind.Filter,
            block.StartOffset,
            1,
            null,
            block.StartOffset,
            New.StructuredSequence.Empty,
            Region(11))
        {
            HandlerBlock = block.Id,
            FilterBlock = block.Id,
        };
        groups = groups.SetItem(new(9), groups[new(9)] with
        {
            Clauses = [filterClause],
            NormalContinuations = [continuation, continuation with { Id = new(1) }],
            ContinuationDispatcher = new New.StructuredDispatcher(
                null,
                [],
                [new New.StructuredDispatcherExit(block.Id, Region(10))]),
        });

        var body = new New.StructuredSequence(
        [
            new New.StructuredIf(occurrence, Region(0), Region(1)),
            new New.StructuredLoop(occurrence, true, Region(2), Region(3), Region(4)),
            new New.StructuredPostTestLoop(Region(5), occurrence, false, Region(6), Region(7)),
            new New.StructuredDispatcher(null, [], [new(block.Id, Region(8))]),
            new New.StructuredExceptionRegion(new(9), null),
        ]);

        ((New.IStructuredExceptionValidator)new New.StructuredExceptionValidator()).Validate(method with
        {
            Body = body,
            ExceptionGroups = groups,
        });
    }

    [Fact]
    public void StackContractRejectsEveryInconsistentStackFact()
    {
        var method = SimpleMethod();
        var block = Assert.Single(method.Blocks.Values);
        var offset = Assert.Single(method.InstructionEntryStacks.Keys);

        Assert.Throws<InvalidOperationException>(() => ValidateStacks(method with
        {
            Header = method.Header with { MaxStack = 0 },
            Blocks = method.Blocks.SetItem(block.Id, block with { EntryStack = [CliValueKind.I4] }),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateStacks(method with
        {
            InstructionEntryStacks = method.InstructionEntryStacks.Add(99, []),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateStacks(method with
        {
            Blocks = method.Blocks.SetItem(block.Id, block with
            {
                Instructions = [I(offset, CilOperation.Nop)],
                Exit = new New.StructuredTerminalExit(I(offset, CilOperation.Return)),
            }),
        }));
        Assert.Throws<InvalidOperationException>(() => ValidateStacks(method with
        {
            InstructionEntryStacks = ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty,
        }));

        var binaryStack = ImmutableDictionary<int, ImmutableArray<CliValueKind>>.Empty
            .Add(0, [CliValueKind.I4, CliValueKind.I8]);
        var binary = method with
        {
            Header = method.Header with { MaxStack = 2 },
            Blocks = method.Blocks.SetItem(block.Id, block with
            {
                Exit = new New.StructuredConditionalExit(
                    new(0, CilOperation.BranchIfEqual, 0, CliValueKind.I4, CliValueKind.I8),
                    block.Id,
                    block.Id),
            }),
            InstructionEntryStacks = binaryStack,
        };
        ValidateStacks(binary);
        Assert.Throws<InvalidOperationException>(() => ValidateStacks(WithCondition(binary, c => c with
        {
            Operation = CilOperation.Nop,
        })));
        Assert.Throws<InvalidOperationException>(() => ValidateStacks(WithCondition(binary, c => c with
        {
            StackSlot = 1,
        })));
        Assert.Throws<InvalidOperationException>(() => ValidateStacks(WithCondition(binary, c => c with
        {
            LeftKind = CliValueKind.I8,
        })));
        Assert.Throws<InvalidOperationException>(() => ValidateStacks(WithCondition(binary, c => c with
        {
            RightKind = null,
        })));
        Assert.Throws<InvalidOperationException>(() => ValidateStacks(WithCondition(binary, c => c with
        {
            RightKind = CliValueKind.F4,
        })));

        var unary = WithCondition(binary with
        {
            Header = binary.Header with { MaxStack = 1 },
            InstructionEntryStacks = binaryStack.SetItem(0, [CliValueKind.I4]),
        }, c => c with
        {
            Operation = CilOperation.BranchIfTrue,
            RightKind = null,
        });
        ValidateStacks(unary);
        Assert.Throws<InvalidOperationException>(() => ValidateStacks(WithCondition(unary, c => c with
        {
            RightKind = CliValueKind.I4,
        })));

        Assert.Throws<InvalidOperationException>(() => ValidateStacks(method with
        {
            Header = method.Header with { MaxStack = 1 },
            Blocks = method.Blocks.SetItem(block.Id, block with { Exit = new New.StructuredLeaveExit(0, block.Id) }),
            InstructionEntryStacks = method.InstructionEntryStacks.SetItem(0, [CliValueKind.I4]),
        }));
    }

    [Fact]
    public void TargetContractAcceptsEverySupportedStructuredRegionShape()
    {
        var method = SimpleMethod();
        var block = Assert.Single(method.Blocks.Values);
        var occurrence = new New.StructuredBlockOccurrence(
            block.Id,
            New.StructuredBlockRole.Owner);

        ValidateTargets(method with
        {
            Blocks = method.Blocks.SetItem(
                block.Id,
                block with { Exit = new New.StructuredFallthroughExit(block.Id) }),
        });
        var branchTargetId = new New.StructuredBlockId(block.Id.Value + 1);
        var branchTarget = block with
        {
            Id = branchTargetId,
            StartOffset = 1,
        };
        var instruction = new CilInstruction(
            0,
            1,
            CilOperation.Branch,
            new CilOperand.BranchTarget(branchTarget.StartOffset));
        ValidateTargets(method with
        {
            Header = method.Header with { Instructions = [instruction] },
            Blocks = method.Blocks
                .SetItem(
                    block.Id,
                    block with
                    {
                        Instructions = [instruction],
                        Exit = new New.StructuredBranchExit(instruction.Offset, branchTarget.Id),
                    })
                .Add(branchTarget.Id, branchTarget),
        });
        var condition = new New.StructuredCondition(
            0,
            CilOperation.BranchIfTrue,
            0,
            CliValueKind.I4,
            null);
        var conditionalInstruction = new CilInstruction(
            0,
            1,
            CilOperation.BranchIfTrue,
            new CilOperand.BranchTarget(block.StartOffset));
        ValidateTargets(method with
        {
            Header = method.Header with { Instructions = [conditionalInstruction] },
            Blocks = method.Blocks.SetItem(
                block.Id,
                block with
                {
                    Instructions = [conditionalInstruction],
                    Exit = new New.StructuredConditionalExit(condition, block.Id, block.Id),
                }),
            Body = new(
            [
                new New.StructuredIf(
                    occurrence,
                    new([]),
                    new([])),
            ]),
        });

        var group = RootedGroup(method) with
        {
            Clauses =
            [
                new New.StructuredExceptionClause(
                    CilExceptionRegionKind.Filter,
                    0,
                    1,
                    null,
                    0,
                    new([]),
                    new([new New.StructuredCode(occurrence)]))
                {
                    HandlerBlock = block.Id,
                },
            ],
        };
        ValidateTargets(method with
        {
            Body = new(
            [
                new New.StructuredExceptionRegion(group.Id, null),
                new New.StructuredExceptionRegion(group.Id, null),
            ]),
            TopLevelExceptionGroups = [group.Id],
            ExceptionGroups = method.ExceptionGroups.SetItem(group.Id, group),
        });
    }

    private static New.StructuredMethod SimpleMethod()
        => Structurize(Body(CliValueKind.Void, 0, [], I(0, CilOperation.Return)));

    private static New.StructuredMethod WithSingleExit(New.StructuredMethod method, New.StructuredBlockExit exit)
    {
        var block = Assert.Single(method.Blocks.Values);
        return method with { Blocks = method.Blocks.SetItem(block.Id, block with { Exit = exit }) };
    }

    private static New.StructuredMethod WithSourceExit(
        New.StructuredMethod method,
        CilInstruction instruction,
        New.StructuredBlockExit exit)
    {
        var block = Assert.Single(method.Blocks.Values);
        return method with
        {
            Header = method.Header with { Instructions = [instruction] },
            Blocks = method.Blocks.SetItem(block.Id, block with
            {
                Instructions = exit is New.StructuredFallthroughExit ? [instruction] : [],
                Exit = exit,
                EndOffset = instruction.NextOffset,
            }),
        };
    }

    private static New.StructuredMethod WithCondition(
        New.StructuredMethod method,
        Func<New.StructuredCondition, New.StructuredCondition> change)
    {
        var block = Assert.Single(method.Blocks.Values);
        var exit = Assert.IsType<New.StructuredConditionalExit>(block.Exit);
        return WithSingleExit(method, exit with { Condition = change(exit.Condition) });
    }

    private static New.StructuredExceptionGroup RootedGroup(New.StructuredMethod method) => new(
            new(0),
            null,
            0,
            1,
            [new New.StructuredExceptionCode(New.StructuredSequence.Empty)],
            [],
            [],
            null,
            null)
    {
        ProtectedBlocks = [method.EntryBlock],
    };

    private static void AssertInvalidGroup(New.StructuredMethod method, New.StructuredExceptionGroup group)
    {
        var groupKey = new New.StructuredExceptionGroupId(0);
        var candidate = method with
        {
            Body = new([new New.StructuredExceptionRegion(groupKey, null)]),
            ExceptionGroups = ImmutableDictionary<New.StructuredExceptionGroupId, New.StructuredExceptionGroup>.Empty
                .Add(groupKey, group),
        };
        Assert.Throws<InvalidOperationException>(() =>
            ((New.IStructuredExceptionValidator)new New.StructuredExceptionValidator()).Validate(candidate));
    }

    private static void ValidateTargets(New.StructuredMethod method) =>
        ((New.IStructuredTargetValidator)new New.StructuredTargetValidator()).Validate(method);

    private static void ValidateStacks(New.StructuredMethod method) =>
        ((New.IStructuredStackContractValidator)new New.StructuredStackContractValidator()).Validate(method);

    private static void ValidateExceptions(New.StructuredMethod method) =>
        ((New.IStructuredExceptionValidator)new New.StructuredExceptionValidator()).Validate(method);

    private sealed record UnsupportedBlockExit() : New.StructuredBlockExit;

    private sealed class OwnershipProbe(List<string> calls) : New.IStructuredBlockOwnershipValidator
    {
        public void Validate(New.StructuredMethod method) => calls.Add("ownership");
    }

    private sealed class InstructionProbe(List<string> calls) :
        New.IStructuredInstructionContractValidator
    {
        public void Validate(New.StructuredMethod method) => calls.Add("instructions");
    }

    private sealed class TargetProbe(List<string> calls) : New.IStructuredTargetValidator
    {
        public void Validate(New.StructuredMethod method) => calls.Add("targets");
    }

    private sealed class ExceptionProbe(List<string> calls) : New.IStructuredExceptionValidator
    {
        public void Validate(New.StructuredMethod method) => calls.Add("exceptions");
    }

    private sealed class StackProbe(List<string> calls) : New.IStructuredStackContractValidator
    {
        public void Validate(New.StructuredMethod method) => calls.Add("stacks");
    }
}
