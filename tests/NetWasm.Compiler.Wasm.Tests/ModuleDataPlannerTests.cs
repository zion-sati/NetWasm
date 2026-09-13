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
            ["z-generic", "a-generic"]);

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
            []);

        Assert.Empty(plan.ExceptionMetadata);
        Assert.Empty(plan.FilterFunclets);
        Assert.Empty(plan.StaticInitializerGuards);
        Assert.Empty(plan.DataSegments);
        Assert.Equal(256, plan.StaticDataEnd);
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
            []);

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
            []);

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
                []));

        Assert.Equal(DiagnosticCode.UnsupportedCil, exception.Diagnostic.Code);
        Assert.Contains("may contain only filters and catches", exception.Message);
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
        IReadOnlyList<string> constructedInitializers);

    private static readonly ModuleDataPlannerCall BuildThroughContract =
        static (planner, methods, directInitializers, constructedInitializers) =>
            planner.Build(
            methods.Select((method, index) => new StructuredMethodEmission(
                new ManagedMethodIdentity($"test-method:{index}"),
                method)), directInitializers, constructedInitializers);
}
