using System.Collections.Immutable;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class ModuleDataPlannerTests
{
    [Fact]
    public void EmptyPlanSingletonRetainsItsStableShape()
    {
        var plan = ModuleDataPlan.Empty;

        Assert.Empty(plan.ExceptionMetadata);
        Assert.Empty(plan.FilterFunclets);
        Assert.Empty(plan.DataSegments);
        Assert.Empty(plan.StaticInitializerGuards);
        Assert.Equal(0, plan.StaticDataEnd);
        Assert.Null(plan.NativeCallbackReadinessAddress);
    }

    [Fact]
    public void StaticInitializerGuardsFollowStableOrderAfterLayoutData()
    {
        var later = Key(0x06000020);
        var earlier = Key(0x06000010);

        var plan = BuildThroughContract(
            new ModuleDataPlanner(
                new RecordingLayoutProvider(),
                new RecordingLayoutProvider(),
                new ExceptionGroupEnumerator(),
                new StructuredExceptionGroupKeyFactory()),
            [],
            [later, earlier],
            ["z-generic", "a-generic"],
            false);

        Assert.Equal(256, plan.StaticInitializerGuards[
            StaticInitializerGuard.KeyFor(earlier)].Address);
        Assert.Equal(260, plan.StaticInitializerGuards[
            StaticInitializerGuard.KeyFor(later)].Address);
        Assert.Equal(264, plan.StaticInitializerGuards["a-generic"].Address);
        Assert.Equal(268, plan.StaticInitializerGuards["z-generic"].Address);
        Assert.Equal(272, plan.StaticDataEnd);
        Assert.Equal([256, 260, 264, 268],
            plan.DataSegments.Select(segment => segment.Address));
        Assert.All(plan.DataSegments, segment =>
            Assert.True(segment.Data.AsSpan().SequenceEqual("\0\0\0\0"u8)));
    }

    [Fact]
    public void EmptyPlanRetainsLayoutStaticDataBoundary()
    {
        var plan = BuildThroughContract(
            new ModuleDataPlanner(
                new RecordingLayoutProvider(),
                new RecordingLayoutProvider(),
                new ExceptionGroupEnumerator(),
                new StructuredExceptionGroupKeyFactory()),
            [],
            [],
            [],
            false);

        Assert.Empty(plan.ExceptionMetadata);
        Assert.Empty(plan.FilterFunclets);
        Assert.Empty(plan.StaticInitializerGuards);
        Assert.Empty(plan.DataSegments);
        Assert.Equal(256, plan.StaticDataEnd);
    }

    [Fact]
    public void CallbackReadinessReservesOneZeroInitializedAlignedWord()
    {
        var plan = BuildThroughContract(
            new ModuleDataPlanner(
                new RecordingLayoutProvider(),
                new RecordingLayoutProvider(),
                new ExceptionGroupEnumerator(),
                new StructuredExceptionGroupKeyFactory()),
            [],
            [],
            [],
            true);

        Assert.Equal(256, plan.NativeCallbackReadinessAddress);
        Assert.Equal(260, plan.StaticDataEnd);
        var segment = Assert.Single(plan.DataSegments);
        Assert.Equal(256, segment.Address);
        Assert.True(segment.Data.AsSpan().SequenceEqual("\0\0\0\0"u8));
        Assert.Empty(plan.StaticInitializerGuards);
    }

    [Fact]
    public void StackTraceSymbolsReserveOneRegistrationGuardBeforeUtf16Names()
    {
        var stackTrace = new StackTraceMethodPlan(
            ImmutableDictionary<EntityKey, int>.Empty,
            ImmutableDictionary<string, int>.Empty,
            [new(7, "Trace")],
            ImmutableDictionary<int, ImmutableArray<StackTraceLocationSymbol>>.Empty,
            29,
            31);

        var plan = new ModuleDataPlanner(
            new RecordingLayoutProvider(),
            new RecordingLayoutProvider(),
            new ExceptionGroupEnumerator(),
            new StructuredExceptionGroupKeyFactory()).Build(
                [],
                [],
                [],
                false,
                stackTrace);

        Assert.Equal(256, plan.StackTraceSymbolRegistrationGuardAddress);
        Assert.Equal(270, plan.StaticDataEnd);
        Assert.Collection(plan.DataSegments,
            segment =>
            {
                Assert.Equal(256, segment.Address);
                Assert.True(segment.Data.AsSpan().SequenceEqual("\0\0\0\0"u8));
            },
            segment =>
            {
                Assert.Equal(260, segment.Address);
                Assert.Equal("Trace", System.Text.Encoding.Unicode.GetString(
                    segment.Data.AsSpan()));
            });
        Assert.Equal(new StackTraceSymbolData(7, 260, 5),
            Assert.Single(plan.StackTraceSymbols));
    }

    [Fact]
    public void CatchGroupsWriteExceptionTypeMetadataAndAdvanceData()
    {
        var program = new FakeProgram();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var empty = ExceptionGroup(method, 0);
        var catches = ExceptionGroup(
            method,
            1,
            Clause(method, CilExceptionRegionKind.Catch, TypeKey),
            Clause(method, CilExceptionRegionKind.Catch, StringTypeKey));
        method = WithExceptionGroups(method, empty, catches);

        var plan = BuildThroughContract(
            new ModuleDataPlanner(
                new RecordingLayoutProvider(),
                new RecordingLayoutProvider(),
                new ExceptionGroupEnumerator(),
                new StructuredExceptionGroupKeyFactory()),
            [method],
            [],
            [],
            false);

        var keys = new StructuredExceptionGroupKeyFactory();
        Assert.Equal(
            new ExceptionGroupMetadata(0, 0, false),
            plan.ExceptionMetadata[keys.Create(method, empty.Id)]);
        Assert.Equal(
            new ExceptionGroupMetadata(256, 2, false),
            plan.ExceptionMetadata[keys.Create(method, catches.Id)]);
        Assert.Single(plan.DataSegments);
        Assert.Equal(264, plan.StaticDataEnd);
        Assert.Equal(
            [9, 0, 0, 0, 9, 0, 0, 0],
            plan.DataSegments[0].Data.ToArray());
    }

    [Fact]
    public void FilteredGroupsWriteCatchAndFilterEntriesAndCreateFunclets()
    {
        var program = new FakeProgram();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var filtered = ExceptionGroup(
            method,
            0,
            Clause(method, CilExceptionRegionKind.Catch, TypeKey),
            Clause(method, CilExceptionRegionKind.Filter, null));
        method = WithExceptionGroups(method, filtered);

        var plan = BuildThroughContract(
            new ModuleDataPlanner(
                new RecordingLayoutProvider(),
                new RecordingLayoutProvider(),
                new ExceptionGroupEnumerator(),
                new StructuredExceptionGroupKeyFactory()),
            [method],
            [],
            [],
            false);

        var metadata = plan.ExceptionMetadata[
            new StructuredExceptionGroupKeyFactory().Create(method, filtered.Id)];
        Assert.Equal(new ExceptionGroupMetadata(256, unchecked((int)0x80000002), true), metadata);
        var funclet = Assert.Single(plan.FilterFunclets);
        Assert.Equal(1, funclet.Id);
        Assert.Same(method, funclet.Method);
        Assert.Same(filtered.Clauses[1], funclet.Clause);
        Assert.Equal(280, plan.StaticDataEnd);
        Assert.Equal(6 * sizeof(int), plan.DataSegments[0].Data.Length);
        Assert.Equal(0, BitConverter.ToInt32(plan.DataSegments[0].Data.AsSpan(0, 4)));
        Assert.Equal(9, BitConverter.ToInt32(plan.DataSegments[0].Data.AsSpan(4, 4)));
        Assert.Equal(0, BitConverter.ToInt32(plan.DataSegments[0].Data.AsSpan(8, 4)));
        Assert.Equal(1, BitConverter.ToInt32(plan.DataSegments[0].Data.AsSpan(12, 4)));
        Assert.Equal(1, BitConverter.ToInt32(plan.DataSegments[0].Data.AsSpan(16, 4)));
    }

    [Fact]
    public void FilteredGroupsRejectUnsupportedFinallyClauses()
    {
        var program = new FakeProgram();
        var method = Structure(
            program,
            program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var invalid = ExceptionGroup(
            method,
            0,
            Clause(method, CilExceptionRegionKind.Filter, null),
            Clause(method, CilExceptionRegionKind.Finally, null));
        method = WithExceptionGroups(method, invalid);

        var exception = Assert.Throws<CompilerException>(() =>
            BuildThroughContract(
                new ModuleDataPlanner(
                    new RecordingLayoutProvider(),
                    new RecordingLayoutProvider(),
                    new ExceptionGroupEnumerator(),
                    new StructuredExceptionGroupKeyFactory()),
                [method],
                [],
                [],
                false));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Contains("may contain only filters and catches", exception.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CatchTablesResolveClosedIdentitiesInsteadOfTheirSharedDefinition(bool includeFilter)
    {
        var program = new FakeProgram();
        var method = Structure(program, program.GetMethod(EntryKey),
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(1, CilOperation.Return));
        var definition = CliTypeIdentity.FromDefinition(program.GetTypeDefinition(TypeKey));
        var first = CliTypeIdentity.GenericInstantiation(definition,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var second = CliTypeIdentity.GenericInstantiation(definition,
            [CliTypeIdentity.Primitive("i8", CliValueKind.I8)]);
        var clauses = new List<StructuredExceptionClause>
        {
            Clause(method, CilExceptionRegionKind.Catch, TypeKey) with { CatchTypeIdentity = first },
            Clause(method, CilExceptionRegionKind.Catch, TypeKey) with { CatchTypeIdentity = second },
        };
        if (includeFilter)
            clauses.Add(Clause(method, CilExceptionRegionKind.Filter, null));
        var group = ExceptionGroup(method, 0, [.. clauses]);
        method = WithExceptionGroups(method, group);
        var layouts = new ClosedCatchLayouts(first, second);

        var plan = BuildThroughContract(new ModuleDataPlanner(new RecordingLayoutProvider(),
            layouts, new ExceptionGroupEnumerator(),
            new StructuredExceptionGroupKeyFactory()), [method], [], [], false);

        var data = Assert.Single(plan.DataSegments).Data;
        Assert.Equal(41, BitConverter.ToInt32(data.AsSpan(includeFilter ? 4 : 0, 4)));
        Assert.Equal(42, BitConverter.ToInt32(data.AsSpan(includeFilter ? 16 : 4, 4)));
        Assert.Equal(2, layouts.Calls);
        Assert.Equal(includeFilter ? 1 : 0, plan.FilterFunclets.Length);
    }

    private sealed class ClosedCatchLayouts(CliTypeIdentity first, CliTypeIdentity second) : ITypeLayoutProvider
    {
        internal int Calls;
        public int ReferenceArrayTypeId => throw new NotSupportedException();
        public int StringTypeId => throw new NotSupportedException();
        public int TypeTypeId => throw new NotSupportedException();
        public ObjectLayout GetObjectLayout(EntityKey type) => throw new NotSupportedException();
        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout) =>
            throw new NotSupportedException();
        public ObjectLayout GetObjectLayout(CliTypeIdentity type)
        {
            Assert.Equal(Calls == 0 ? first : second, type);
            return new(41 + Calls++, 16, []);
        }
    }

    private static StructuredExceptionGroup ExceptionGroup(
        StructuredMethod method,
        int id,
        params StructuredExceptionClause[] clauses) => new(
        new StructuredExceptionGroupId(id),
        null,
        0,
        0,
        [],
        [.. clauses],
        [],
        null,
        null)
        {
            ProtectedBlocks = [],
        };

    private static StructuredExceptionClause Clause(
        StructuredMethod method,
        CilExceptionRegionKind kind,
        EntityKey? catchType) => new(
        kind,
        0,
        1,
        catchType,
        kind == CilExceptionRegionKind.Filter ? 0 : null,
        StructuredSequence.Empty,
        kind == CilExceptionRegionKind.Filter ? StructuredSequence.Empty : null)
        {
            HandlerBlock = method.EntryBlock,
            FilterBlock = kind == CilExceptionRegionKind.Filter ? method.EntryBlock : null,
        };

    private delegate ModuleDataPlan ModuleDataPlannerCall(
        IModuleDataPlanner planner,
        IEnumerable<StructuredMethod> methods,
        IReadOnlyList<EntityKey> directInitializers,
        IReadOnlyList<string> constructedInitializers,
        bool reserveNativeCallbackReadiness);

    private static readonly ModuleDataPlannerCall BuildThroughContract =
        static (planner, methods, directInitializers, constructedInitializers,
            reserveNativeCallbackReadiness) =>
            planner.Build(
            methods.Select((method, index) => new StructuredMethodEmission(
                new ManagedMethodIdentity($"test-method:{index}"),
                method)), directInitializers, constructedInitializers,
                reserveNativeCallbackReadiness);
}
