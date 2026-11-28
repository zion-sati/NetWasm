// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class TerminalBranchValueFramePlannerTests
{
    private readonly ITerminalBranchValueFramePlanner _planner =
        new[] { new TerminalBranchValueFramePlanner(CreateArgumentSignatureTypes(new FakeProgram())) }
            .Cast<ITerminalBranchValueFramePlanner>().Single();
    private readonly MethodRootMap _roots = new(EntryKey, [], []);

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    public void ContinuationChainMustReachTheNextTailBlock(int shape, bool accepted)
    {
        var method = Method();
        StructuredBlockExit exit = shape switch
        {
            0 or 5 => new StructuredFallthroughExit(new(4)),
            1 => new StructuredBranchExit(32, new(4)),
            2 => new StructuredFallthroughExit(new(2)),
            3 => new StructuredBranchExit(32, new(2)),
            6 => new StructuredFallthroughExit(null),
            _ => new StructuredTerminalExit(I(32, CilOperation.Return)),
        };
        method = method with
        {
            Blocks = method.Blocks
                .SetItem(new(3), method.Blocks[new(3)] with { Exit = exit })
                .Add(new(4), Block(4, [], new StructuredTerminalExit(I(40, CilOperation.Return)))),
            Body = new([method.Body.Regions[0], Code(3),
                shape == 5 ? StructuredSequence.Empty : Code(4)]),
        };

        var plan = _planner.Plan(method, Layout(), _roots, copyTails: true);

        if (!accepted)
        {
            Assert.Null(plan);
            return;
        }
        Assert.NotNull(plan);
        Assert.Equal([new StructuredBlockId(3), new StructuredBlockId(4)],
            plan.WhenTrue.Regions.Skip(1).Cast<StructuredCode>().Select(code => code.Occurrence.Block));
        Assert.All(plan.WhenFalse.Regions.Skip(1).Cast<StructuredCode>(),
            code => Assert.Equal(StructuredBlockRole.ExecutingReplica, code.Occurrence.Role));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnsupportedRegionInEitherArmRejectsPlacement(bool trueArm)
    {
        var method = Method();
        var branch = (StructuredIf)method.Body.Regions[0];
        var unsupported = new StructuredSequence([new StructuredExceptionRegion(new(0), null)]);
        branch = trueArm ? branch with { WhenTrue = unsupported } : branch with { WhenFalse = unsupported };
        method = method with { Body = new([branch, Code(3)]) };

        Assert.Null(_planner.Plan(method, Layout(), _roots, copyTails: true));
    }

    [Fact]
    public void UnsupportedRegionInTheTailRejectsPlacement()
    {
        var method = Method();
        method = method with { Body = new([method.Body.Regions[0], new StructuredExceptionRegion(new(0), null)]) };

        Assert.Null(_planner.Plan(method, Layout(), _roots, copyTails: true));
    }

    // A root map that names an offset no block of the method holds leaves the frame where it is.
    [Fact]
    public void ARootSafepointOutsideTheMethodsBlocksRejectsPlacement()
    {
        var source = new RootSource(RootSourceKind.Local, 0);
        var roots = _roots with
        {
            Slots = ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
            Safepoints = ImmutableDictionary<int, SafepointRootMap>.Empty.Add(50, new(50, [source], [])),
        };

        Assert.Null(_planner.Plan(Method(), Layout(), roots, copyTails: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectsFrameUsesInsidePreAndPostTestLoops(bool postTest)
    {
        var method = Method();
        var branch = (StructuredIf)method.Body.Regions[0];
        var condition = new StructuredBlockOccurrence(new(4), StructuredBlockRole.Owner);
        StructuredRegion loop = postTest
            ? new StructuredPostTestLoop(branch.WhenTrue, condition, true, StructuredSequence.Empty,
                new([new StructuredLoopBreak()]))
            : new StructuredLoop(condition, true, branch.WhenTrue,
                new([new StructuredLoopContinue()]), StructuredSequence.Empty);
        method = method with
        {
            Blocks = method.Blocks.Add(new(4), Block(4, [], new StructuredFallthroughExit(new(1)))),
            Body = new([branch with { WhenTrue = new([loop]) }, Code(3)]),
        };

        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, Layout(), _roots, copyTails: true));

        Assert.True(plan.FramedWhenTrue);
        Assert.Same(loop, plan.WhenTrue.Regions[0]);
    }

    // A flattened else-if chain in an arm is a labeled block its conditions branch to the end of: the branches stay in
    // the arm, so the plan takes it. A branch to a label the arm did not open would leave the framed arm without
    // releasing its frame, so the plan does not.
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ALabeledBlockInAnArmTakesOnlyItsOwnLabelsBranches(bool outsideLabel, bool accepted)
    {
        var method = Method();
        var branch = (StructuredIf)method.Body.Regions[0];
        var chain = new StructuredLabeledBlock(new(5), new([
            new StructuredIf(new(new(4), StructuredBlockRole.Owner),
                new([new StructuredLabelBranch(outsideLabel ? new(9) : new(5))]), StructuredSequence.Empty),
            Code(1),
        ]));
        method = method with
        {
            Blocks = method.Blocks.Add(new(4), Block(4, [], new StructuredFallthroughExit(new(1)))),
            Body = new([branch with { WhenTrue = new([chain]) }, Code(3)]),
        };

        var plan = _planner.Plan(method, Layout(), _roots, copyTails: true);

        if (!accepted)
        {
            Assert.Null(plan);
            return;
        }
        Assert.True(Assert.IsType<TerminalBranchValueFramePlan>(plan).FramedWhenTrue);
        Assert.Same(chain, plan.WhenTrue.Regions[0]);
    }

    [Fact]
    public void DispatcherContinuationDoesNotIntroduceAnotherStorageOwner()
    {
        var method = Method();
        var branch = (StructuredIf)method.Body.Regions[0];
        branch = branch with { WhenTrue = new([Code(1), new StructuredDispatcherContinue(new(1))]) };
        method = method with { Body = new([branch, Code(3)]) };

        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, Layout(), _roots, copyTails: true));

        Assert.True(plan.FramedWhenTrue);
        Assert.IsType<StructuredDispatcherContinue>(plan.WhenTrue.Regions[1]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollectsNestedConditionalAndDispatcherStorageUses(bool dispatcher)
    {
        var method = Method();
        var branch = (StructuredIf)method.Body.Regions[0];
        StructuredRegion nested = dispatcher
            ? new StructuredDispatcher(new(1),
                [new(new(new(1), StructuredBlockRole.Owner), new(5), null)],
                [new(new(5), new([Code(5)]))])
            : new StructuredIf(new(new(4), StructuredBlockRole.Owner),
                new([Code(1)]), new([Code(5)]));
        method = method with
        {
            Blocks = method.Blocks
                .Add(new(4), Block(4, [], new StructuredFallthroughExit(new(1))))
                .Add(new(5), Block(5, [], new StructuredFallthroughExit(new(3)))),
            Body = new([branch with { WhenTrue = new([nested]) }, Code(3)]),
        };

        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, Layout(), _roots, copyTails: true));

        Assert.True(plan.FramedWhenTrue);
        Assert.Same(nested, plan.WhenTrue.Regions[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfinesOriginalStorageToEitherArmAndDuplicatesTheTerminalContinuation(bool reverse)
    {
        var method = Method();
        var branch = (StructuredIf)method.Body.Regions[0];
        if (reverse)
        {
            branch = branch with { WhenTrue = branch.WhenFalse, WhenFalse = branch.WhenTrue };
            method = method with { Body = new([branch, method.Body.Regions[1]]) };
        }

        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, Layout(), _roots, copyTails: true));

        Assert.Equal(!reverse, plan.FramedWhenTrue);
        Assert.Equal(branch.Condition, plan.Condition);
        Assert.Equal([0], plan.IncomingScalarLocals);
        Assert.Same(branch.WhenTrue.Regions[0], plan.WhenTrue.Regions[0]);
        Assert.Same(branch.WhenFalse.Regions[0], plan.WhenFalse.Regions[0]);
        Assert.Same(method.Body.Regions[1], plan.WhenTrue.Regions[1]);
        var replica = Assert.IsType<StructuredCode>(plan.WhenFalse.Regions[1]);
        Assert.Equal(new StructuredBlockId(3), replica.Occurrence.Block);
        Assert.Equal(StructuredBlockRole.ExecutingReplica, replica.Occurrence.Role);
        Assert.Equal(StructuredBlockRole.Owner,
            ((StructuredCode)method.Body.Regions[1]).Occurrence.Role);
    }

    // Regions before the if that need no frame (a method's argument checks) run without one, and the if places
    // the frame in its arm as a leading if would.
    [Fact]
    public void AnIfAfterRegionsThatNeedNoFramePlacesItsFrameAfterThem()
    {
        var method = WithPrefix(Method(), I(40, CilOperation.Nop));

        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, Layout(), _roots, copyTails: true));

        Assert.Same(method.Body.Regions[0], Assert.Single(plan.UnframedPrefix!.Regions));
        Assert.Equal(((StructuredIf)method.Body.Regions[1]).Condition, plan.Condition);
        Assert.True(plan.FramedWhenTrue);
    }

    // A region before the if that takes the framed local's address needs the frame from the method's entry.
    [Fact]
    public void ARegionBeforeTheIfThatNeedsTheFrameKeepsItAtTheEntry() =>
        Assert.Null(_planner.Plan(WithPrefix(Method(), I(40, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
            Layout(), _roots, copyTails: true));

    // When both arms leave the method, no tail follows the branch for a frame to move into: nothing is placed.
    [Fact]
    public void ArmsThatBothLeaveTheMethodPlaceNoFrame()
    {
        var method = Method();
        method = method with
        {
            Blocks = method.Blocks
                .SetItem(new(1), method.Blocks[new(1)] with { Exit = new StructuredTerminalExit(I(12, CilOperation.Return)) })
                .SetItem(new(2), method.Blocks[new(2)] with { Exit = new StructuredTerminalExit(I(22, CilOperation.Return)) }),
        };

        Assert.Null(_planner.Plan(method, Layout(), _roots, copyTails: true));
    }

    [Theory]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.I8)]
    [InlineData(CliValueKind.F4)]
    [InlineData(CliValueKind.F8)]
    [InlineData(CliValueKind.NativeInt)]
    public void PreservesPrimitiveEntryValuesWithoutRewritingTheirAddressConsumers(CliValueKind kind)
    {
        var method = Method();
        method = method with
        {
            Header = method.Header with
            {
                Locals = [kind],
                LocalSignatureTypes = [CliTypeIdentity.FromStackKind(kind)],
            },
            Blocks = method.Blocks.SetItem(new(0), method.Blocks[new(0)] with
            {
                Instructions = method.Blocks[new(0)].Instructions.SetItem(0,
                    I(0, CilOperation.DefaultValue, new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(kind)))),
            }),
        };
        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, Layout(), _roots, copyTails: true));
        Assert.Contains(0, plan.IncomingScalarLocals);
        Assert.Same(((StructuredIf)method.Body.Regions[0]).WhenTrue.Regions[0], plan.WhenTrue.Regions[0]);
    }

    [Fact]
    public void RejectsNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(null!, Layout(), _roots, copyTails: true));
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(Method(), null!, _roots, copyTails: true));
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(Method(), Layout(), null!, copyTails: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void RejectsStorageNotOwnedByOneLocalBranch(int shape)
    {
        var layout = Layout();
        layout = shape switch
        {
            0 => layout with { Size = 0 },
            2 => layout with { NativeArgumentOffsets = ImmutableDictionary<(int, int), int>.Empty.Add((10, 0), 0) },
            3 => layout with { MemberResultOffsets = ImmutableDictionary<int, int>.Empty.Add(10, 0) },
            4 => layout with { CallReceiverOffsets = ImmutableDictionary<int, int>.Empty.Add(10, 0) },
            5 => layout with { LocalOffsets = [] },
            6 => layout with { TemporaryOffsets = ImmutableDictionary<int, int>.Empty.Add(999, 0) },
            7 => layout with { TemporaryOffsets = ImmutableDictionary<int, int>.Empty.Add(20, 0) },
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        Assert.Null(_planner.Plan(Method(), layout, _roots, copyTails: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsAddressExposureBeforeOrOutsideTheFramedArm(int block)
    {
        var method = Method();
        var original = method.Blocks[new(block)];
        method = method with
        {
            Blocks = method.Blocks.SetItem(new(block), original with
            {
                Instructions = original.Instructions.Add(I(100, CilOperation.LoadLocalAddress, new CilOperand.Index(0))),
            }),
        };

        Assert.Null(_planner.Plan(method, Layout(), _roots, copyTails: true));
    }

    [Theory]
    [InlineData(CliValueKind.ManagedAddress)]
    [InlineData(CliValueKind.ManagedReference)]
    [InlineData(CliValueKind.ValueType)]
    public void DoesNotTreatReferencesOrAggregatesAsPrimitiveTransfers(CliValueKind kind)
    {
        var method = Method();
        method = method with { Header = method.Header with { LocalSignatureTypes = [CliTypeIdentity.FromStackKind(kind)] } };
        Assert.Null(_planner.Plan(method, Layout(), _roots, copyTails: true));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void RejectsNonterminalOrStackCarryingContinuations(int shape)
    {
        var method = Method();
        var terminal = method.Blocks[new(3)];
        method = shape switch
        {
            0 => method with { Body = new([method.Body.Regions[0]]) },
            1 => method with { Body = new([method.Body.Regions[1], method.Body.Regions[0]]) },
            2 => method with { Body = new([method.Body.Regions[0], StructuredSequence.Empty]) },
            3 => method with { Blocks = method.Blocks.SetItem(new(3), terminal with { EntryStack = [CliValueKind.I4] }) },
            4 => method with { Blocks = method.Blocks.SetItem(new(3), terminal with { Exit = new StructuredBranchExit(31, new(0)) }) },
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        Assert.Null(_planner.Plan(method, Layout(), _roots, copyTails: true));
    }

    [Fact]
    public void RejectsDynamicallySizedStackStorage()
    {
        var method = Method();
        method = method with
        {
            Blocks = method.Blocks.SetItem(new(1), method.Blocks[new(1)] with
            {
                Instructions = [I(10, CilOperation.LocalAllocate)],
            })
        };
        Assert.Null(_planner.Plan(method, Layout(), _roots, copyTails: true));
    }

    [Fact]
    public void RejectsStorageWithNoObservedUse()
    {
        var method = Method();
        method = method with { Blocks = method.Blocks.SetItem(new(1), method.Blocks[new(1)] with { Instructions = [] }) };
        Assert.Null(_planner.Plan(method, Layout(), _roots, copyTails: true));
    }

    // An argument the frame holds is copied in where the framed arm begins; the other arm and the
    // condition read the parameter, so neither may take the argument's address.
    [Fact]
    public void AnArgumentOnlyTheFramedArmAddressesIsCopiedInThere()
    {
        var plan = _planner.Plan(WithAddressedArgument(CliTypeIdentity.FromStackKind(CliValueKind.I8), 1),
            WithArgument(Layout()), _roots, copyTails: true);

        Assert.True(Assert.IsType<TerminalBranchValueFramePlan>(plan).FramedWhenTrue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsAnArgumentAddressedOutsideTheFramedArm(int block) =>
        Assert.Null(_planner.Plan(WithAddressedArgument(CliTypeIdentity.FromStackKind(CliValueKind.I8), block),
            WithArgument(Layout()), _roots, copyTails: true));

    // A value-type argument's parameter becomes the address of its frame copy, which the
    // unframed arm would not have.
    [Fact]
    public void RejectsAValueTypeArgumentInTheFrame() =>
        Assert.Null(_planner.Plan(WithAddressedArgument(CliTypeIdentity.Named(Assembly, "Test", "Pair", isValueType: true), 1),
            WithArgument(Layout()), _roots, copyTails: true));

    private static StructuredMethod WithAddressedArgument(CliTypeIdentity type, int block)
    {
        var method = Method();
        var original = method.Blocks[new(block)];
        return method with
        {
            Header = method.Header with
            {
                Method = method.Header.Method with
                {
                    Signature = new(CliTypeIdentity.FromStackKind(CliValueKind.Void), [type]),
                },
            },
            Blocks = method.Blocks.SetItem(new(block), original with
            {
                Instructions =
                [
                    .. original.Instructions,
                    I(block * 10 + 5, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
                    I(block * 10 + 6, CilOperation.Pop),
                ],
            }),
        };
    }

    private static ValueFrameLayout WithArgument(ValueFrameLayout layout) => layout with
    {
        Size = 24,
        ArgumentOffsets = ImmutableDictionary<int, int>.Empty.Add(0, 16),
    };

    private static ValueFrameLayout Layout() => new(16,
        ImmutableDictionary<int, int>.Empty.Add(0, 0), [], [], [0]);

    [Theory]
    [InlineData("i1")]
    [InlineData("u1")]
    [InlineData("i2")]
    [InlineData("u2")]
    [InlineData("bool")]
    [InlineData("char")]
    public void DoesNotLoseNarrowStorageNormalizationWhenKeepingAValueInARegister(string primitive)
    {
        var method = Method();
        method = method with
        {
            Header = method.Header with
            {
                Locals = [CliValueKind.I4],
                LocalSignatureTypes = [CliTypeIdentity.Primitive(primitive, CliValueKind.I4)],
            }
        };
        Assert.Null(_planner.Plan(method, Layout(), _roots, copyTails: true));
    }

    [Theory]
    [InlineData(RootSourceKind.Local, 0, true)]
    [InlineData(RootSourceKind.ManagedAddressLocal, 0, true)]
    [InlineData(RootSourceKind.StackStorageAnchor, 0, true)]
    [InlineData(RootSourceKind.Argument, 0, false)]
    [InlineData(RootSourceKind.ManagedAddressArgument, 0, false)]
    [InlineData(RootSourceKind.EvaluationStack, 0, false)]
    [InlineData(RootSourceKind.Local, 1, false)]
    public void UnframedRootPublicationCannotReadUnmaterializedStorage(RootSourceKind kind, int index, bool rejected)
    {
        var source = new RootSource(kind, index);
        var roots = _roots with
        {
            Slots = ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
            Safepoints = ImmutableDictionary<int, SafepointRootMap>.Empty.Add(20, new(20, [source], [])),
        };
        Assert.Equal(rejected, _planner.Plan(Method(), Layout(), roots, copyTails: true) is null);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(20, true)]
    [InlineData(30, true)]
    [InlineData(32, true)]
    [InlineData(10, false)]
    public void ChecksConstructorRootsAtEntryConditionsAndTerminalExits(int offset, bool rejected)
    {
        var source = new RootSource(RootSourceKind.StackStorageAnchor, 0);
        var roots = _roots with
        {
            Slots = ImmutableDictionary<RootSource, int>.Empty.Add(source, 0),
            Safepoints = ImmutableDictionary<int, SafepointRootMap>.Empty.Add(offset, new(offset, [], [source])),
        };
        Assert.Equal(rejected, _planner.Plan(Method(), Layout(), roots, copyTails: true) is null);
    }

    // Where tails are not copied, a frame that holds only roots, all of them at safepoints in one arm,
    // is left where that arm ends and the code after the if runs without it; with copies allowed,
    // each arm carries the straight-line tail as before.
    [Fact]
    public void WithoutTailCopiesAFrameHoldingOnlyRootsIsLeftWhereItsArmEnds()
    {
        var (method, roots) = RootedArm(10);

        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, NoValueFrame, roots, copyTails: false));
        var copied = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, NoValueFrame, roots, copyTails: true));

        Assert.True(plan is { FramedWhenTrue: true, DefersRootFrame: true });
        Assert.Equal([new StructuredBlockId(1)], plan.WhenTrue.Regions.Cast<StructuredCode>().Select(code => code.Occurrence.Block));
        Assert.Equal([new StructuredBlockId(2)], plan.WhenFalse.Regions.Cast<StructuredCode>().Select(code => code.Occurrence.Block));
        Assert.Equal([new StructuredBlockId(3)], plan.UnframedTail!.Regions.Cast<StructuredCode>().Select(code => code.Occurrence.Block));
        Assert.Null(copied.UnframedTail);
        Assert.Equal(2, copied.WhenTrue.Regions.Length);
    }

    // A root held at a safepoint in the tail, the condition or the other arm needs the frame there too,
    // and a value frame's contents would be lost with it.
    [Theory]
    [InlineData(30)]
    [InlineData(20)]
    [InlineData(2)]
    public void ARootHeldOutsideTheFramedArmKeepsTheFrameForTheMethod(int safepoint)
    {
        var (method, roots) = RootedArm(10);
        var argument = roots.Slots.Keys.Single();
        roots = roots with { Safepoints = roots.Safepoints.Add(safepoint, new(safepoint, [argument], [])) };

        Assert.Null(_planner.Plan(method, NoValueFrame, roots, copyTails: false));
    }

    // Without tail copies a value frame stays with the method even where one arm returns: placed in
    // the other, it would leave its roots to the root frame's publication.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WithoutTailCopiesAValueFrameIsNeverMoved(bool armReturns)
    {
        var method = Method();
        if (armReturns)
        {
            method = method with
            {
                Blocks = method.Blocks.SetItem(new(2), Block(2, [I(20, CilOperation.LoadLocal, new CilOperand.Index(0)),
                    I(21, CilOperation.Pop)], new StructuredTerminalExit(I(22, CilOperation.Return)))),
            };
        }

        Assert.Null(_planner.Plan(method, Layout(), _roots, copyTails: false));
        Assert.NotNull(_planner.Plan(method, Layout(), _roots, copyTails: true));
    }

    private static readonly ValueFrameLayout NoValueFrame = new(0, ImmutableDictionary<int, int>.Empty, [], [], []);

    // The method with a call where its true arm took the local's address, at whose safepoint argument 0 is a root.
    private (StructuredMethod Method, MethodRootMap Roots) RootedArm(int safepoint)
    {
        var method = Method();
        method = method with
        {
            Blocks = method.Blocks.SetItem(new(1), Block(1, [I(10, CilOperation.Nop), I(11, CilOperation.Nop)],
                new StructuredBranchExit(12, new(3)))),
        };
        var argument = new RootSource(RootSourceKind.Argument, 0);
        return (method, _roots with
        {
            Slots = ImmutableDictionary<RootSource, int>.Empty.Add(argument, 0),
            Safepoints = ImmutableDictionary<int, SafepointRootMap>.Empty.Add(safepoint, new(safepoint, [argument], [])),
        });
    }

    private static StructuredMethod Method()
    {
        var definition = new MethodDefinitionModel(EntryKey, TypeKey, "Run", true,
            MethodSignatureModel.Create(CliValueKind.Void), 1);
        var blocks = new[]
        {
            Block(0, [I(0, CilOperation.LoadInt64, new CilOperand.ConstantI8(7)),
                I(1, CilOperation.StoreLocal, new CilOperand.Index(0)), I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1))],
                new StructuredConditionalExit(new(3, CilOperation.BranchIfTrue, 0, CliValueKind.I4, null), new(1), new(2))),
            Block(1, [I(10, CilOperation.LoadLocalAddress, new CilOperand.Index(0)), I(11, CilOperation.Pop)], new StructuredBranchExit(12, new(3))),
            Block(2, [I(20, CilOperation.LoadLocal, new CilOperand.Index(0)), I(21, CilOperation.Pop)], new StructuredBranchExit(22, new(3))),
            Block(3, [I(30, CilOperation.LoadLocal, new CilOperand.Index(0)), I(31, CilOperation.Pop)], new StructuredTerminalExit(I(32, CilOperation.Return))),
        }.ToImmutableDictionary(block => block.Id);
        return new(new(definition, null, 2, [CliValueKind.I8],
                [CliTypeIdentity.FromStackKind(CliValueKind.I8)], [.. blocks.Values.SelectMany(block => block.Instructions)]),
            new(0), blocks,
            new([new StructuredIf(new(new(0), StructuredBlockRole.Owner),
                new([Code(1)]), new([Code(2)])), Code(3)]), [], [], []);
    }

    // The method with a block before its if: the instruction, then the if's condition block.
    private static StructuredMethod WithPrefix(StructuredMethod method, CilInstruction instruction) => method with
    {
        Header = method.Header with { Instructions = [instruction, .. method.Header.Instructions] },
        EntryBlock = new(4),
        Blocks = method.Blocks.Add(new(4), Block(4, instruction.Operation == CilOperation.LoadLocalAddress
            ? [instruction, I(41, CilOperation.Pop)] : [instruction], new StructuredFallthroughExit(new(0)))),
        Body = new([Code(4), .. method.Body.Regions]),
    };

    private static StructuredCode Code(int id) => new(new(new(id), StructuredBlockRole.Owner));

    private static StructuredBlockDefinition Block(int id, ImmutableArray<CilInstruction> instructions,
        StructuredBlockExit exit) => new(new(id), id * 10, instructions, [], exit) { EndOffset = id * 10 + 9 };
}
