// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.LoopInvariantFields;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.LoopInvariantFields;

public sealed class AggregateFieldHoistingPlannerTests
{
    private static readonly AssemblyIdentity Assembly = new("FieldHoistingPlanner.Tests");
    private static readonly CliTypeIdentity Aggregate = CliTypeIdentity.Named(
        Assembly,
        "Fixtures",
        "Aggregate",
        isValueType: true);
    private static readonly CliTypeIdentity Int32 =
        CliTypeIdentity.FromStackKind(CliValueKind.I4);

    [Theory]
    [InlineData(LoopInvariantFieldHoistingPolicy.Conservative)]
    [InlineData(LoopInvariantFieldHoistingPolicy.NullChecks)]
    public void PlanHoistsStableFieldReadIntoExistingPreheader(LoopInvariantFieldHoistingPolicy policy)
    {
        var field = Field("Count", Int32, 1);
        var body = LoopBody(field);

        var plan = Create(new FixedProjectionResolver(), policy).Plan(body);

        var hoist = Assert.Single(plan.Hoists);
        Assert.Equal(2, hoist.PreheaderTerminatorOffset);
        Assert.Equal(0, hoist.SourceLocalIndex);
        Assert.Equal(1, hoist.CachedLocalIndex);
        Assert.Same(Int32, hoist.FieldType);
        Assert.Collection(hoist.AddressOffsets, offset => Assert.Equal(3, offset));
        Assert.Collection(hoist.ProjectionOffsets, offset => Assert.Equal(4, offset));
        Assert.Equal(CilOperation.LoadLocalAddress, hoist.AddressLoad.Operation);
        Assert.Equal(CilOperation.LoadField, hoist.Projection.Operation);
    }

    [Fact]
    public void PlanHoistsIndependentFieldsFromTheSameStableAggregate()
    {
        var count = Field("Count", Int32, 1);
        var values = Field("Values", CliTypeIdentity.SzArray(Int32), 2);
        var body = LoopBody(count) with
        {
            Instructions =
            [
                .. LoopBody(count).Instructions.Take(5),
                new CilInstruction(
                    5,
                    6,
                    CilOperation.LoadLocalAddress,
                    new CilOperand.Index(0)),
                new CilInstruction(
                    6,
                    7,
                    CilOperation.LoadField,
                    new CilOperand.FieldInstance(values)),
                new CilInstruction(7, 8, CilOperation.Pop, new CilOperand.None()),
                new CilInstruction(8, 9, CilOperation.Nop, new CilOperand.None()),
                new CilInstruction(9, 10, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                new CilInstruction(10, 11, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
                new CilInstruction(11, 12, CilOperation.Return, new CilOperand.None()),
            ],
        };

        var plan = Create(new FixedProjectionResolver()).Plan(body);

        Assert.Equal(2, plan.Hoists.Length);
        Assert.Equal([1, 2], plan.Hoists.Select(hoist => hoist.CachedLocalIndex));
        Assert.Equal([4, 6], plan.Hoists.Select(hoist => hoist.Projection.Offset));
    }

    [Theory]
    [InlineData(RejectedShape.Reassignment)]
    [InlineData(RejectedShape.UnknownAddressUse)]
    [InlineData(RejectedShape.ExceptionRegion)]
    [InlineData(RejectedShape.NoUniquePreheader)]
    [InlineData(RejectedShape.NoInitialization)]
    public void PlanRejectsUnprovedAggregateLifetime(RejectedShape shape)
    {
        var field = Field("Count", Int32, 1);
        var body = shape switch
        {
            RejectedShape.Reassignment => ReassignedLoopBody(field),
            RejectedShape.UnknownAddressUse => UnknownAddressLoopBody(field),
            RejectedShape.ExceptionRegion => LoopBody(field) with
            {
                ExceptionRegions =
                [
                    new CilExceptionRegion(
                        CilExceptionRegionKind.Finally,
                        0,
                        3,
                        3,
                        6,
                        null,
                        null),
                ],
            },
            RejectedShape.NoUniquePreheader => LoopBody(field) with
            {
                Instructions =
                [
                    .. LoopBody(field).Instructions.Take(2),
                    new CilInstruction(
                        2,
                        3,
                        CilOperation.BranchIfTrue,
                        new CilOperand.BranchTarget(7)),
                    .. LoopBody(field).Instructions.Skip(3),
                ],
            },
            RejectedShape.NoInitialization => LoopBody(field) with
            {
                Instructions =
                [
                    new CilInstruction(0, 1, CilOperation.Nop, new CilOperand.None()),
                    new CilInstruction(1, 2, CilOperation.Nop, new CilOperand.None()),
                    .. LoopBody(field).Instructions.Skip(2),
                ],
            },
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        Assert.Empty(Create(new FixedProjectionResolver()).Plan(body).Hoists);
    }

    [Fact]
    public void PlanRejectsAddressUseWhoseProjectionIsNotProved()
    {
        var field = Field("Count", Int32, 1);
        var resolver = new FixedProjectionResolver(rejectOffset: 4);

        Assert.Empty(Create(resolver).Plan(LoopBody(field)).Hoists);
    }

    [Theory]
    [InlineData(MalformedAddressShape.OutOfRangeLocal)]
    [InlineData(MalformedAddressShape.BlockBoundary)]
    [InlineData(MalformedAddressShape.NoncontiguousProjection)]
    public void PlanRejectsStructurallyUnprovedAddressUse(MalformedAddressShape shape)
    {
        var field = Field("Count", Int32, 1);
        var body = MalformedAddressBody(field, shape);

        Assert.Empty(Create(new FixedProjectionResolver()).Plan(body).Hoists);
    }

    [Theory]
    [InlineData(UnstableStoreShape.InLoop)]
    [InlineData(UnstableStoreShape.DoesNotDominatePreheader)]
    public void PlanRejectsSingleStoreWithoutProvedLifetime(UnstableStoreShape shape)
    {
        var field = Field("Count", Int32, 1);
        var body = shape switch
        {
            UnstableStoreShape.InLoop => InLoopStoreBody(field),
            UnstableStoreShape.DoesNotDominatePreheader =>
                NonDominatingStoreBody(field),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        Assert.Empty(Create(new FixedProjectionResolver()).Plan(body).Hoists);
    }

    [Theory]
    [InlineData(PreheaderShape.EntryLoop)]
    [InlineData(PreheaderShape.MultiplePreheaders)]
    [InlineData(PreheaderShape.BranchingPreheader)]
    public void PlanRejectsLoopWithoutUniqueUnconditionalPreheader(PreheaderShape shape)
    {
        var body = shape switch
        {
            PreheaderShape.EntryLoop => EntryLoopBody(),
            PreheaderShape.MultiplePreheaders => MultiplePreheadersBody(),
            PreheaderShape.BranchingPreheader => BranchingPreheaderBody(),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };

        Assert.Empty(Create(new FixedProjectionResolver()).Plan(body).Hoists);
    }

    [Fact]
    public void PlanClaimsNestedLoopOccurrenceOnlyOnce()
    {
        var plan = Create(new FixedProjectionResolver()).Plan(
            NestedLoopBody(Field("Count", Int32, 1)));

        var hoist = Assert.Single(plan.Hoists);
        Assert.Equal(3, hoist.PreheaderTerminatorOffset);
        Assert.Equal([4], hoist.AddressOffsets.ToArray());
        Assert.Equal([5], hoist.ProjectionOffsets.ToArray());
    }

    [Fact]
    public void DisabledPolicyReturnsNoPlanWithoutReadingControlFlow()
    {
        var planner = new AggregateFieldHoistingPlanner(
            new ThrowingGraphBuilderFactory(),
            new ControlFlowGraphAnalyzerFactory(),
            new FixedProjectionResolver(),
            LoopInvariantFieldHoistingPolicy.Disabled);

        Assert.Empty(planner.Plan(LoopBody(Field("Count", Int32, 1))).Hoists);
    }

    [Fact]
    public void PlanReturnsEmptyForAcyclicBody()
    {
        var body = new CilMethodBody(
            Method(),
            0,
            [],
            [new CilInstruction(0, 1, CilOperation.Return, new CilOperand.None())])
        {
            LocalSignatureTypes = [],
        };

        Assert.Empty(Create(new FixedProjectionResolver()).Plan(body).Hoists);
    }

    [Fact]
    public void PlannerRejectsMissingOrInvalidInputs()
    {
        var builders = new ControlFlowGraphBuilderFactory();
        var analyzers = new ControlFlowGraphAnalyzerFactory();
        var projections = new FixedProjectionResolver();

        Assert.Throws<ArgumentNullException>(() => new AggregateFieldHoistingPlanner(
            null!, analyzers, projections, LoopInvariantFieldHoistingPolicy.Conservative));
        Assert.Throws<ArgumentNullException>(() => new AggregateFieldHoistingPlanner(
            builders, null!, projections, LoopInvariantFieldHoistingPolicy.Conservative));
        Assert.Throws<ArgumentNullException>(() => new AggregateFieldHoistingPlanner(
            builders, analyzers, null!, LoopInvariantFieldHoistingPolicy.Conservative));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AggregateFieldHoistingPlanner(
            builders, analyzers, projections, (LoopInvariantFieldHoistingPolicy)255));
        Assert.Throws<ArgumentNullException>(() =>
            Create(projections).Plan(null!));
    }

    private static AggregateFieldHoistingPlanner Create(
        IFieldProjectionResolver projections,
        LoopInvariantFieldHoistingPolicy policy = LoopInvariantFieldHoistingPolicy.Conservative) => new(
            new ControlFlowGraphBuilderFactory(),
            new ControlFlowGraphAnalyzerFactory(),
            projections,
            policy);

    private static CilMethodBody LoopBody(FieldInstanceModel field) => Body(
        new CilInstruction(
            0,
            1,
            CilOperation.DefaultValue,
            new CilOperand.TypeIdentity(Aggregate)),
        new CilInstruction(1, 2, CilOperation.StoreLocal, new CilOperand.Index(0)),
        new CilInstruction(2, 3, CilOperation.Branch, new CilOperand.BranchTarget(7)),
        new CilInstruction(3, 4, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
        new CilInstruction(4, 5, CilOperation.LoadField, new CilOperand.FieldInstance(field)),
        new CilInstruction(5, 6, CilOperation.Pop, new CilOperand.None()),
        new CilInstruction(6, 7, CilOperation.Nop, new CilOperand.None()),
        new CilInstruction(7, 8, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(8, 9, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
        new CilInstruction(9, 10, CilOperation.Return, new CilOperand.None()));

    private static CilMethodBody ReassignedLoopBody(FieldInstanceModel field) => Body(
        new CilInstruction(
            0,
            1,
            CilOperation.DefaultValue,
            new CilOperand.TypeIdentity(Aggregate)),
        new CilInstruction(1, 2, CilOperation.StoreLocal, new CilOperand.Index(0)),
        new CilInstruction(2, 3, CilOperation.Branch, new CilOperand.BranchTarget(8)),
        new CilInstruction(3, 4, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
        new CilInstruction(4, 5, CilOperation.LoadField, new CilOperand.FieldInstance(field)),
        new CilInstruction(5, 6, CilOperation.Pop, new CilOperand.None()),
        new CilInstruction(6, 7, CilOperation.StoreLocal, new CilOperand.Index(0)),
        new CilInstruction(7, 8, CilOperation.Nop, new CilOperand.None()),
        new CilInstruction(8, 9, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(9, 10, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
        new CilInstruction(10, 11, CilOperation.Return, new CilOperand.None()));

    private static CilMethodBody UnknownAddressLoopBody(FieldInstanceModel field) => Body(
        new CilInstruction(
            0,
            1,
            CilOperation.DefaultValue,
            new CilOperand.TypeIdentity(Aggregate)),
        new CilInstruction(1, 2, CilOperation.StoreLocal, new CilOperand.Index(0)),
        new CilInstruction(2, 3, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
        new CilInstruction(3, 4, CilOperation.Pop, new CilOperand.None()),
        new CilInstruction(4, 5, CilOperation.Branch, new CilOperand.BranchTarget(9)),
        new CilInstruction(5, 6, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
        new CilInstruction(6, 7, CilOperation.LoadField, new CilOperand.FieldInstance(field)),
        new CilInstruction(7, 8, CilOperation.Pop, new CilOperand.None()),
        new CilInstruction(8, 9, CilOperation.Nop, new CilOperand.None()),
        new CilInstruction(9, 10, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(10, 11, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
        new CilInstruction(11, 12, CilOperation.Return, new CilOperand.None()));

    private static CilMethodBody MalformedAddressBody(
        FieldInstanceModel field,
        MalformedAddressShape shape)
    {
        var body = LoopBody(field);
        var instructions = body.Instructions.ToBuilder();
        instructions[3] = shape switch
        {
            MalformedAddressShape.OutOfRangeLocal => new CilInstruction(
                3,
                4,
                CilOperation.LoadLocalAddress,
                new CilOperand.Index(1)),
            MalformedAddressShape.BlockBoundary => instructions[3],
            MalformedAddressShape.NoncontiguousProjection => new CilInstruction(
                3,
                5,
                CilOperation.LoadLocalAddress,
                new CilOperand.Index(0)),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
        if (shape == MalformedAddressShape.BlockBoundary)
        {
            instructions[2] = new CilInstruction(
                2,
                3,
                CilOperation.BranchIfTrue,
                new CilOperand.BranchTarget(4));
        }

        return body with { Instructions = instructions.ToImmutable() };
    }

    private static CilMethodBody InLoopStoreBody(FieldInstanceModel field) => Body(
        new CilInstruction(0, 1, CilOperation.Branch, new CilOperand.BranchTarget(1)),
        new CilInstruction(
            1,
            2,
            CilOperation.DefaultValue,
            new CilOperand.TypeIdentity(Aggregate)),
        new CilInstruction(2, 3, CilOperation.StoreLocal, new CilOperand.Index(0)),
        new CilInstruction(3, 4, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
        new CilInstruction(4, 5, CilOperation.LoadField, new CilOperand.FieldInstance(field)),
        new CilInstruction(5, 6, CilOperation.Pop, new CilOperand.None()),
        new CilInstruction(6, 7, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(7, 8, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(1)),
        new CilInstruction(8, 9, CilOperation.Return, new CilOperand.None()));

    private static CilMethodBody NonDominatingStoreBody(FieldInstanceModel field) => Body(
        new CilInstruction(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(1, 2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
        new CilInstruction(
            2,
            3,
            CilOperation.DefaultValue,
            new CilOperand.TypeIdentity(Aggregate)),
        new CilInstruction(3, 4, CilOperation.StoreLocal, new CilOperand.Index(0)),
        new CilInstruction(4, 5, CilOperation.Branch, new CilOperand.BranchTarget(9)),
        new CilInstruction(5, 6, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
        new CilInstruction(6, 7, CilOperation.LoadField, new CilOperand.FieldInstance(field)),
        new CilInstruction(7, 8, CilOperation.Pop, new CilOperand.None()),
        new CilInstruction(8, 9, CilOperation.Nop, new CilOperand.None()),
        new CilInstruction(9, 10, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(10, 11, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
        new CilInstruction(11, 12, CilOperation.Return, new CilOperand.None()));

    private static CilMethodBody EntryLoopBody() => Body(
        new CilInstruction(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(1, 2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(0)),
        new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None()));

    private static CilMethodBody MultiplePreheadersBody() => Body(
        new CilInstruction(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(1, 2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
        new CilInstruction(2, 3, CilOperation.Branch, new CilOperand.BranchTarget(5)),
        new CilInstruction(3, 5, CilOperation.Branch, new CilOperand.BranchTarget(5)),
        new CilInstruction(5, 6, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(6, 7, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
        new CilInstruction(7, 8, CilOperation.Return, new CilOperand.None()));

    private static CilMethodBody BranchingPreheaderBody() => Body(
        new CilInstruction(0, 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(1, 2, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
        new CilInstruction(2, 3, CilOperation.Return, new CilOperand.None()),
        new CilInstruction(3, 4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(4, 5, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
        new CilInstruction(5, 6, CilOperation.Return, new CilOperand.None()));

    private static CilMethodBody NestedLoopBody(FieldInstanceModel field) => Body(
        new CilInstruction(
            0,
            1,
            CilOperation.DefaultValue,
            new CilOperand.TypeIdentity(Aggregate)),
        new CilInstruction(1, 2, CilOperation.StoreLocal, new CilOperand.Index(0)),
        new CilInstruction(2, 3, CilOperation.Branch, new CilOperand.BranchTarget(10)),
        new CilInstruction(3, 4, CilOperation.Branch, new CilOperand.BranchTarget(4)),
        new CilInstruction(4, 5, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
        new CilInstruction(5, 6, CilOperation.LoadField, new CilOperand.FieldInstance(field)),
        new CilInstruction(6, 7, CilOperation.Pop, new CilOperand.None()),
        new CilInstruction(7, 8, CilOperation.Nop, new CilOperand.None()),
        new CilInstruction(8, 9, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(9, 10, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
        new CilInstruction(10, 11, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
        new CilInstruction(11, 12, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
        new CilInstruction(12, 13, CilOperation.Return, new CilOperand.None()));

    private static CilMethodBody Body(params CilInstruction[] instructions) => new(
        Method(),
        1,
        [CliValueKind.ValueType],
        [.. instructions])
    {
        LocalSignatureTypes = [Aggregate],
    };

    private static MethodDefinitionModel Method() => new(
        new(Assembly, 0x06000001),
        new(Assembly, 0x02000002),
        "Run",
        IsStatic: true,
        MethodSignatureModel.Create(CliValueKind.Void),
        RelativeVirtualAddress: 1);

    private static FieldInstanceModel Field(
        string name,
        CliTypeIdentity type,
        int token) => new(
        new FieldDefinitionModel(
            new(Assembly, 0x04000000 + token),
            new(Assembly, 0x02000001),
            name,
            type,
            isStatic: false),
        Aggregate,
        type);

    public enum RejectedShape
    {
        Reassignment,
        UnknownAddressUse,
        ExceptionRegion,
        NoUniquePreheader,
        NoInitialization,
    }

    public enum MalformedAddressShape
    {
        OutOfRangeLocal,
        BlockBoundary,
        NoncontiguousProjection,
    }

    public enum UnstableStoreShape
    {
        InLoop,
        DoesNotDominatePreheader,
    }

    public enum PreheaderShape
    {
        EntryLoop,
        MultiplePreheaders,
        BranchingPreheader,
    }

    private sealed class FixedProjectionResolver(int? rejectOffset = null) :
        IFieldProjectionResolver
    {
        public FieldProjection? Resolve(
            CliTypeIdentity aggregateType,
            CilInstruction instruction) =>
            instruction.Offset == rejectOffset ||
            instruction.Operation != CilOperation.LoadField ||
            instruction.Operand is not CilOperand.FieldInstance field
                ? null
                : new FieldProjection(instruction, field.Value);
    }

    private sealed class ThrowingGraphBuilderFactory : IControlFlowGraphBuilderFactory
    {
        public IControlFlowGraphBuilder Create() =>
            throw new InvalidOperationException("Control flow must not be read.");
    }
}
