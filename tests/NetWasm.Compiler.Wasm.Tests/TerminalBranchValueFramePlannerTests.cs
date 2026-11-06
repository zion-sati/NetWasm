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

        var plan = _planner.Plan(method, Layout(), _roots);

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

        Assert.Null(_planner.Plan(method, Layout(), _roots));
    }

    [Fact]
    public void UnsupportedRegionInTheTailRejectsPlacement()
    {
        var method = Method();
        method = method with { Body = new([method.Body.Regions[0], new StructuredExceptionRegion(new(0), null)]) };

        Assert.Null(_planner.Plan(method, Layout(), _roots));
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

        Assert.Null(_planner.Plan(Method(), Layout(), roots));
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

        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, Layout(), _roots));

        Assert.True(plan.FramedWhenTrue);
        Assert.Same(loop, plan.WhenTrue.Regions[0]);
    }

    [Fact]
    public void DispatcherContinuationDoesNotIntroduceAnotherStorageOwner()
    {
        var method = Method();
        var branch = (StructuredIf)method.Body.Regions[0];
        branch = branch with { WhenTrue = new([Code(1), new StructuredDispatcherContinue(new(1))]) };
        method = method with { Body = new([branch, Code(3)]) };

        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, Layout(), _roots));

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

        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, Layout(), _roots));

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

        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, Layout(), _roots));

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
        var plan = Assert.IsType<TerminalBranchValueFramePlan>(_planner.Plan(method, Layout(), _roots));
        Assert.Contains(0, plan.IncomingScalarLocals);
        Assert.Same(((StructuredIf)method.Body.Regions[0]).WhenTrue.Regions[0], plan.WhenTrue.Regions[0]);
    }

    [Fact]
    public void RejectsNullInputs()
    {
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(null!, Layout(), _roots));
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(Method(), null!, _roots));
        Assert.Throws<ArgumentNullException>(() => _planner.Plan(Method(), Layout(), null!));
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

        Assert.Null(_planner.Plan(Method(), layout, _roots));
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

        Assert.Null(_planner.Plan(method, Layout(), _roots));
    }

    [Theory]
    [InlineData(CliValueKind.ManagedAddress)]
    [InlineData(CliValueKind.ManagedReference)]
    [InlineData(CliValueKind.ValueType)]
    public void DoesNotTreatReferencesOrAggregatesAsPrimitiveTransfers(CliValueKind kind)
    {
        var method = Method();
        method = method with { Header = method.Header with { LocalSignatureTypes = [CliTypeIdentity.FromStackKind(kind)] } };
        Assert.Null(_planner.Plan(method, Layout(), _roots));
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
        Assert.Null(_planner.Plan(method, Layout(), _roots));
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
        Assert.Null(_planner.Plan(method, Layout(), _roots));
    }

    [Fact]
    public void RejectsStorageWithNoObservedUse()
    {
        var method = Method();
        method = method with { Blocks = method.Blocks.SetItem(new(1), method.Blocks[new(1)] with { Instructions = [] }) };
        Assert.Null(_planner.Plan(method, Layout(), _roots));
    }

    // An argument the frame holds is copied in where the framed arm begins; the other arm and the
    // condition read the parameter, so neither may take the argument's address.
    [Fact]
    public void AnArgumentOnlyTheFramedArmAddressesIsCopiedInThere()
    {
        var plan = _planner.Plan(WithAddressedArgument(CliTypeIdentity.FromStackKind(CliValueKind.I8), 1),
            WithArgument(Layout()), _roots);

        Assert.True(Assert.IsType<TerminalBranchValueFramePlan>(plan).FramedWhenTrue);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(3)]
    public void RejectsAnArgumentAddressedOutsideTheFramedArm(int block) =>
        Assert.Null(_planner.Plan(WithAddressedArgument(CliTypeIdentity.FromStackKind(CliValueKind.I8), block),
            WithArgument(Layout()), _roots));

    // A value-type argument's parameter becomes the address of its frame copy, which the
    // unframed arm would not have.
    [Fact]
    public void RejectsAValueTypeArgumentInTheFrame() =>
        Assert.Null(_planner.Plan(WithAddressedArgument(CliTypeIdentity.Named(Assembly, "Test", "Pair", isValueType: true), 1),
            WithArgument(Layout()), _roots));

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
        Assert.Null(_planner.Plan(method, Layout(), _roots));
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
        Assert.Equal(rejected, _planner.Plan(Method(), Layout(), roots) is null);
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
        Assert.Equal(rejected, _planner.Plan(Method(), Layout(), roots) is null);
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

    private static StructuredCode Code(int id) => new(new(new(id), StructuredBlockRole.Owner));

    private static StructuredBlockDefinition Block(int id, ImmutableArray<CilInstruction> instructions,
        StructuredBlockExit exit) => new(new(id), id * 10, instructions, [], exit) { EndOffset = id * 10 + 9 };
}
