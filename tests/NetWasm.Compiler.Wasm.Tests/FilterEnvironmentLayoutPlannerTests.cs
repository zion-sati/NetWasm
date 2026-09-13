using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class FilterEnvironmentLayoutPlannerTests
{
    [Fact]
    public void CapturesFilterArgumentsAndLocalsInStableOrder()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var structured = StructureWithLocals(
            program,
            method,
            [CliValueKind.ManagedReference],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(5, CilOperation.Return));
        var clause = ExceptionClause(structured, CilExceptionRegionKind.Filter, true);
        structured = WithExceptionGroups(
            structured,
            ExceptionGroup(structured, 0, clause));
        var values = new ValueFrameLayout(
            0,
            [],
            [],
            [],
            []);

        var layouts = new RecordingLayoutProvider();
        IFilterEnvironmentLayoutPlanner planner =
            new[]
            {
                new FilterEnvironmentLayoutPlanner(
                    layouts,
                    layouts,
                    CreateArgumentSignatureTypes(program),
                    new ExceptionGroupEnumerator()),
            }
            .Cast<IFilterEnvironmentLayoutPlanner>()
            .Single();
        var result = planner.Create(structured, values);

        Assert.Equal(12, result.Size);
        Assert.Equal(1, result.RootSlotCount);
        Assert.Equal(0, result.RootFrameOffset);
        Assert.Equal(4, result.Captures[new CapturedSlot(true, 0)].Offset);
        Assert.Equal(8, result.Captures[new CapturedSlot(false, 0)].Offset);
        var (ByteOffset, RootSlot) = Assert.Single(result.Captures[new CapturedSlot(false, 0)].Roots);
        Assert.Equal(0, ByteOffset);
        Assert.Equal(0, RootSlot);
    }

    [Fact]
    public void MethodWithoutFiltersNeedsNoEnvironment()
    {
        var program = new FakeProgram();
        var structured = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));

        var layouts = new RecordingLayoutProvider();
        IFilterEnvironmentLayoutPlanner planner =
            new[]
            {
                new FilterEnvironmentLayoutPlanner(
                    layouts,
                    layouts,
                    CreateArgumentSignatureTypes(program),
                    new ExceptionGroupEnumerator()),
            }
            .Cast<IFilterEnvironmentLayoutPlanner>()
            .Single();
        var result = planner.Create(
            structured,
            new ValueFrameLayout(
                0,
                [],
                [],
                [],
                []));

        Assert.Same(FilterEnvironmentLayout.Empty, result);
    }

    [Fact]
    public void ScansEveryFilterCaptureOperationAndIgnoresNonFilterClauses()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var structured = StructureWithLocals(
            program,
            method,
            [CliValueKind.I4],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.StoreArgument, new CilOperand.Index(0)),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(3, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(4, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(5, CilOperation.StoreArgument, new CilOperand.Index(0)),
            I(6, CilOperation.LoadArgumentAddress, new CilOperand.Index(0)),
            I(7, CilOperation.Pop),
            I(8, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(9, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(10, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(11, CilOperation.Pop),
            I(12, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(13, CilOperation.StoreArgument, new CilOperand.Index(0)),
            I(14, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(15, CilOperation.Return));
        var catchClause = ExceptionClause(structured, CilExceptionRegionKind.Catch, false);
        var filterWithoutOffset = new StructuredExceptionClause(
            CilExceptionRegionKind.Filter,
            0,
            1,
            null,
            null,
            StructuredSequence.Empty,
            null)
        {
            HandlerBlock = structured.EntryBlock,
        };
        var filter = ExceptionClause(structured, CilExceptionRegionKind.Filter, true);
        structured = WithExceptionGroups(
            structured,
            ExceptionGroup(structured, 0, catchClause, filterWithoutOffset, filter));

        var layouts = new RecordingLayoutProvider();
        IFilterEnvironmentLayoutPlanner planner =
            new[]
            {
                new FilterEnvironmentLayoutPlanner(
                    layouts,
                    layouts,
                    CreateArgumentSignatureTypes(program),
                    new ExceptionGroupEnumerator()),
            }
            .Cast<IFilterEnvironmentLayoutPlanner>()
            .Single();

        var result = planner.Create(
            structured,
            new ValueFrameLayout(0, [], [], [], []));

        Assert.Equal(2, result.Captures.Count);
        Assert.Equal(12, result.Size);
        Assert.Equal(0, result.RootSlotCount);
        Assert.Equal(4, result.Captures[new CapturedSlot(true, 0)].Offset);
        Assert.Equal(8, result.Captures[new CapturedSlot(false, 0)].Offset);
    }

    [Fact]
    public void UsesValueLayoutsForValueTypeArgumentsAndLocals()
    {
        var program = new FakeProgram();
        var method = program.GetMethod(EntryKey);
        var structured = StructureWithLocals(
            program,
            method,
            [CliValueKind.ValueType],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(5, CilOperation.Return));
        var filter = ExceptionClause(structured, CilExceptionRegionKind.Filter, true);
        structured = WithExceptionGroups(
            structured,
            ExceptionGroup(structured, 0, filter));
        var valueType = CliTypeIdentity.Named(
            new AssemblyIdentity("Test"),
            "Tests",
            "Pair",
            isValueType: true);
        var layouts = new RecordingLayoutProvider();
        IFilterEnvironmentLayoutPlanner planner =
            new[]
            {
                new FilterEnvironmentLayoutPlanner(
                    layouts,
                    layouts,
                    new ValueTypeArgumentSignatureResolver(valueType),
                    new ExceptionGroupEnumerator()),
            }
            .Cast<IFilterEnvironmentLayoutPlanner>()
            .Single();

        var result = planner.Create(
            structured,
            new ValueFrameLayout(
                8,
                ImmutableDictionary<int, int>.Empty.Add(0, 4),
                [],
                [],
                []));

        Assert.Equal(2, result.Captures.Count);
        Assert.Equal(16, result.Size);
        Assert.Equal(0, result.RootSlotCount);
        Assert.Equal(12, result.Captures[new CapturedSlot(true, 0)].Offset);
        Assert.Equal(4, result.Captures[new CapturedSlot(false, 0)].Offset);
    }

    [Fact]
    public void TraversesEveryStructuredFilterRegionAndAllowsAMissingFilterBody()
    {
        var program = new FakeProgram();
        var structured = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var occurrence = new StructuredBlockOccurrence(
            structured.EntryBlock,
            StructuredBlockRole.Owner);
        var leaf = new StructuredSequence([new StructuredCode(occurrence)]);
        var body = new StructuredSequence(
        [
            new StructuredIf(occurrence, leaf, leaf),
            new StructuredLoop(occurrence, true, leaf, leaf, leaf),
            new StructuredPostTestLoop(leaf, occurrence, false, leaf, leaf),
            new StructuredDispatcher(
                structured.EntryBlock,
                [new StructuredDispatcherBlock(occurrence, null, null)],
                [new StructuredDispatcherExit(structured.EntryBlock, leaf)]),
            new StructuredExceptionRegion(new StructuredExceptionGroupId(0), null),
            new StructuredLoopBreak(),
            new StructuredLoopContinue(),
            new StructuredDispatcherContinue(structured.EntryBlock),
        ]);
        var filter = ExceptionClause(structured, CilExceptionRegionKind.Filter, true) with
        {
            FilterBody = body,
        };
        var filterWithoutBody = filter with { FilterBody = null };
        structured = WithExceptionGroups(
            structured,
            ExceptionGroup(structured, 0, filterWithoutBody, filter));
        var layouts = new RecordingLayoutProvider();
        var planner = new FilterEnvironmentLayoutPlanner(
            layouts,
            layouts,
            CreateArgumentSignatureTypes(program),
            new ExceptionGroupEnumerator());

        var result = planner.Create(
            structured,
            new ValueFrameLayout(0, [], [], [], []));

        Assert.Empty(result.Captures);
        Assert.Equal(4, result.Size);
    }

    private sealed class ValueTypeArgumentSignatureResolver(CliTypeIdentity type) :
        IArgumentSignatureTypeResolver
    {
        public CliTypeIdentity Resolve(StructuredMethodHeader header, int index) => type;
    }
}
