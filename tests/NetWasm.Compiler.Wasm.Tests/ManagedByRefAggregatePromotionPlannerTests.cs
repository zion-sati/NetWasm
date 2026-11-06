using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedByRefAggregatePromotionPlannerTests
{
    [Fact]
    public void UsesTheInstantiatedMethodSignatureForCandidateParameters()
    {
        var fixture = new Fixture();
        var method = Fixture.Method([fixture.ByRefAggregate], fixture.DirectLoads(4).ToArray());
        var definition = method.Header.Method with
        {
            Signature = MethodSignatureModel.Create(CliValueKind.Void),
        };
        method = method with
        {
            Header = method.Header with
            {
                Method = definition,
                MethodInstance = new(definition, fixture.Aggregate, [], method.Header.Method.Signature),
            },
        };

        var plan = fixture.Plan(method);

        Assert.Equal(0, Assert.Single(plan.PromotedParameters).Key);
        Assert.Equal(4, plan.Projections.Count);
    }

    [Fact]
    public void IncomingBlockStackDoesNotInventAnArgumentOrigin()
    {
        var fixture = new Fixture();
        var steps = new[]
        {
            Step(Fixture.Instruction(0, CilOperation.Pop), CliValueKind.I4),
        }.Concat(fixture.DirectLoads(4, offset: 1)).ToArray();

        var plan = fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps));

        Assert.Single(plan.PromotedParameters);
        Assert.Equal(4, plan.Projections.Count);
        Assert.DoesNotContain(0, plan.Projections.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StoresThroughBothSupportedFieldOperandForms(bool definitionOperand)
    {
        var fixture = new Fixture();
        CilOperand operand = definitionOperand
            ? new CilOperand.Entity(fixture.Field32.Definition.Key)
            : new CilOperand.FieldInstance(fixture.Field32);
        var steps = fixture.DirectLoads(4).Concat([
            Step(Fixture.Instruction(20, CilOperation.LoadArgument, new CilOperand.Index(0))),
            Step(Fixture.Instruction(21, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
                CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(22, CilOperation.StoreField, operand),
                CliValueKind.ManagedAddress, CliValueKind.I4),
        ]).ToArray();

        var plan = fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps));

        Assert.Single(plan.PromotedParameters);
        Assert.Equal(0, plan.Projections[22].ParameterIndex);
        Assert.Equal(0, plan.Projections[22].ByteOffset);
        Assert.Equal(4, plan.Projections[22].Size);
        Assert.False(plan.Projections[22].AddressAlreadyProjected);
    }

    [Theory]
    [InlineData(CilOperation.LoadField)]
    [InlineData(CilOperation.LoadObject)]
    [InlineData(CilOperation.StoreField)]
    public void UntrackedPointerAccessDoesNotInventAnArgumentProjection(CilOperation operation)
    {
        var fixture = new Fixture();
        CilOperand operand = operation == CilOperation.LoadObject
            ? new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.I4))
            : new CilOperand.FieldInstance(fixture.Field32);
        var steps = fixture.DirectLoads(4).ToList();
        steps.Add(Step(Fixture.Instruction(20, CilOperation.LoadArgument, new CilOperand.Index(1))));
        if (operation == CilOperation.StoreField)
        {
            steps.Add(Step(Fixture.Instruction(21, CilOperation.LoadInt32,
                new CilOperand.ConstantI4(7)), CliValueKind.NativeInt));
            steps.Add(Step(Fixture.Instruction(22, operation, operand),
                CliValueKind.NativeInt, CliValueKind.I4));
        }
        else
        {
            steps.Add(Step(Fixture.Instruction(22, operation, operand), CliValueKind.NativeInt));
            steps.Add(Step(Fixture.Instruction(23, CilOperation.Pop), CliValueKind.I4));
        }

        var plan = fixture.Plan(Fixture.Method(
            [fixture.ByRefAggregate, CliTypeIdentity.FromStackKind(CliValueKind.NativeInt)],
            steps.ToArray()));

        Assert.Single(plan.PromotedParameters);
        Assert.DoesNotContain(22, plan.Projections.Keys);
    }

    [Theory]
    [InlineData(CilOperation.LoadField)]
    [InlineData(CilOperation.LoadFieldAddress)]
    [InlineData(CilOperation.StoreField)]
    [InlineData(CilOperation.LoadObject)]
    [InlineData(CilOperation.StoreObject)]
    public void EmptyOriginStackDoesNotInventAProjection(CilOperation operation)
    {
        var fixture = new Fixture();
        var steps = fixture.DirectLoads(4).Append(
            Step(Fixture.Instruction(20, operation))).ToArray();

        var plan = fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps));

        Assert.Single(plan.PromotedParameters);
        Assert.DoesNotContain(20, plan.Projections.Keys);
    }

    [Fact]
    public void StackResynchronizationRejectsAnyLostArgumentOrigin()
    {
        var fixture = new Fixture();
        var steps = fixture.DirectLoads(4).Concat([
            Step(Fixture.Instruction(20, CilOperation.LoadArgument, new CilOperand.Index(0))),
            Step(Fixture.Instruction(21, CilOperation.Nop)),
        ]).ToArray();

        Assert.Empty(fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps)).PromotedParameters);
    }

    [Theory]
    [InlineData(CliValueKind.Void)]
    [InlineData(CliValueKind.I4)]
    public void IndirectCallsRejectPromotionForBothReturnShapes(CliValueKind returnKind)
    {
        var fixture = new Fixture();
        var signature = MethodSignatureModel.Create(returnKind);
        var steps = fixture.DirectLoads(4).Concat([
            Step(Fixture.Instruction(20, CilOperation.LoadInt32, new CilOperand.ConstantI4(0))),
            Step(Fixture.Instruction(21, CilOperation.CallIndirect, new CilOperand.CallSite(signature)),
                CliValueKind.NativeInt),
            returnKind == CliValueKind.Void
                ? Step(Fixture.Instruction(22, CilOperation.Return))
                : Step(Fixture.Instruction(22, CilOperation.Pop), CliValueKind.I4),
        ]).ToArray();

        Assert.Empty(fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps)).PromotedParameters);
    }

    [Fact]
    public void FloatingFieldCannotUseAnIntegerProjection()
    {
        var fixture = new Fixture();
        var field = fixture.Field32 with { FieldType = CliTypeIdentity.FromStackKind(CliValueKind.F4) };
        var steps = fixture.DirectLoads(4).Concat([
            Step(Fixture.Instruction(20, CilOperation.LoadArgument, new CilOperand.Index(0))),
            Step(Fixture.Instruction(21, CilOperation.LoadField, new CilOperand.FieldInstance(field)),
                CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(22, CilOperation.Pop), CliValueKind.F4),
        ]).ToArray();

        Assert.Empty(fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps)).PromotedParameters);
    }

    [Theory]
    [InlineData(-4, 4)]
    [InlineData(1, 4)]
    [InlineData(12, 4)]
    [InlineData(0, 8)]
    public void InvalidFieldLayoutRejectsPromotion(int offset, int size)
    {
        var fixture = new Fixture { FieldOffset = offset, FieldSize = size };

        Assert.Empty(fixture.Plan(Fixture.Method([fixture.ByRefAggregate],
            fixture.DirectLoads(4).ToArray())).PromotedParameters);
    }

    [Fact]
    public void ScalarObjectLayoutMustMatchTheProjectedFieldWidth()
    {
        var fixture = new Fixture { ScalarSize = 8 };
        var steps = fixture.DirectLoads(4).Concat(
            fixture.FieldAddressObjectLoad(20, CliValueKind.I4)).ToArray();

        Assert.Empty(fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps))
            .PromotedParameters);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldOperandsRequireAnInstanceFieldOfTheAggregate(bool definitionOperand)
    {
        var fixture = new Fixture();
        var field = fixture.Field32 with
        {
            Definition = fixture.Field32.Definition with { IsStatic = true },
        };
        fixture.FieldOverride = field.Definition;
        CilOperand operand = definitionOperand
            ? new CilOperand.Entity(field.Definition.Key)
            : new CilOperand.FieldInstance(field);
        var steps = fixture.DirectLoads(4).Concat([
            Step(Fixture.Instruction(20, CilOperation.LoadArgument, new CilOperand.Index(0))),
            Step(Fixture.Instruction(21, CilOperation.LoadField, operand), CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(22, CilOperation.Pop), CliValueKind.I4),
        ]).ToArray();

        Assert.Empty(fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps)).PromotedParameters);
    }

    [Fact]
    public void DoesNotApplyARootFieldOffsetToAnAlreadyProjectedAddress()
    {
        var fixture = new Fixture();
        var field = new CilOperand.FieldInstance(fixture.Field32);
        var steps = fixture.DirectLoads(4).Concat([
            Step(Fixture.Instruction(20, CilOperation.LoadArgument, new CilOperand.Index(0))),
            Step(Fixture.Instruction(21, CilOperation.LoadFieldAddress, field), CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(22, CilOperation.LoadField, field), CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(23, CilOperation.Pop), CliValueKind.I4),
        ]).ToArray();

        Assert.Empty(fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps)).PromotedParameters);
    }

    [Fact]
    public void WholeAggregateObjectLoadCannotBeMistakenForAFieldProjection()
    {
        var fixture = new Fixture();
        var steps = fixture.DirectLoads(4).Concat([
            Step(Fixture.Instruction(20, CilOperation.LoadArgument, new CilOperand.Index(0))),
            Step(Fixture.Instruction(21, CilOperation.LoadObject,
                new CilOperand.TypeIdentity(fixture.Aggregate)), CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(22, CilOperation.Pop), CliValueKind.ValueType),
        ]).ToArray();

        Assert.Empty(fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps)).PromotedParameters);
    }

    [Theory]
    [InlineData(4, 4, false)]
    [InlineData(20, 4, false)]
    [InlineData(10, 4, false)]
    [InlineData(12, 2, false)]
    [InlineData(12, 4, true)]
    public void RejectsLayoutsThatCannotUseIntegerLanes(int size, int alignment, bool byReferences)
    {
        var fixture = new Fixture
        {
            AggregateSize = size,
            AggregateAlignment = alignment,
            AggregateByReferences = byReferences
        };

        Assert.Empty(fixture.Plan(Fixture.Method([fixture.ByRefAggregate],
            fixture.DirectLoads(4).ToArray())).PromotedParameters);
    }

    [Fact]
    public void LoadsAProjectedFieldThroughItsExactObjectType()
    {
        var fixture = new Fixture();
        var steps = fixture.DirectLoads(4).Concat(
            fixture.FieldAddressObjectLoad(20, CliValueKind.I4)).ToArray();

        var plan = fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps));

        Assert.Single(plan.PromotedParameters);
        Assert.True(plan.Projections[22].AddressAlreadyProjected);
        Assert.Equal(4, plan.Projections[22].Size);
    }

    [Theory]
    [InlineData(CilOperation.LoadArgumentAddress)]
    [InlineData(CilOperation.StoreArgument)]
    public void RejectsReassignmentOrAddressEscapeOfThePromotedParameter(CilOperation operation)
    {
        var fixture = new Fixture();
        var steps = fixture.DirectLoads(4).Concat([
            Step(Fixture.Instruction(20, CilOperation.LoadArgument, new CilOperand.Index(0))),
            Step(Fixture.Instruction(21, operation, new CilOperand.Index(0)),
                CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(22, CilOperation.Return)),
        ]).ToArray();

        Assert.Empty(fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps))
            .PromotedParameters);
    }

    [Fact]
    public void DuplicatedFieldAddressRetainsItsProjectionForTwoLoads()
    {
        var fixture = new Fixture();
        var field = new CilOperand.FieldInstance(fixture.Field32);
        var int32 = new CilOperand.TypeIdentity(CliTypeIdentity.FromStackKind(CliValueKind.I4));
        var steps = fixture.DirectLoads(4).Concat([
            Step(Fixture.Instruction(20, CilOperation.LoadArgument, new CilOperand.Index(0))),
            Step(Fixture.Instruction(21, CilOperation.LoadFieldAddress, field), CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(22, CilOperation.Duplicate), CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(23, CilOperation.LoadObject, int32),
                CliValueKind.ManagedAddress, CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(24, CilOperation.Pop), CliValueKind.ManagedAddress, CliValueKind.I4),
            Step(Fixture.Instruction(25, CilOperation.LoadObject, int32), CliValueKind.ManagedAddress),
            Step(Fixture.Instruction(26, CilOperation.Pop), CliValueKind.I4),
        ]).ToArray();

        var plan = fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps));

        Assert.Single(plan.PromotedParameters);
        Assert.True(plan.Projections[23].AddressAlreadyProjected);
        Assert.True(plan.Projections[25].AddressAlreadyProjected);
    }

    [Fact]
    public void PlansDirectAndFieldAddressObjectProjectionsAcrossPureDivRem()
    {
        var fixture = new Fixture();
        var steps = fixture.DirectLoads(4).Concat(
            fixture.FieldAddressStoreAcrossCall(20, fixture.DivRem)).ToArray();

        var plan = fixture.Plan(Fixture.Method([fixture.ByRefAggregate], steps));

        var parameter = Assert.Single(plan.PromotedParameters);
        Assert.Equal(0, parameter.Key);
        Assert.Equal(12, parameter.Value.Size);
        Assert.Equal(5, plan.Projections.Count);
        var objectStore = plan.Projections[25];
        Assert.True(objectStore.AddressAlreadyProjected);
        Assert.Equal(0, objectStore.ByteOffset);
        Assert.Equal(CliValueKind.I4, objectStore.StackKind);
    }

    [Theory]
    [InlineData(Rejection.Disabled)]
    [InlineData(Rejection.InsufficientTraffic)]
    [InlineData(Rejection.MultipleRoots)]
    [InlineData(Rejection.ReferenceBearing)]
    [InlineData(Rejection.ExceptionRegion)]
    [InlineData(Rejection.UnknownCall)]
    [InlineData(Rejection.PointerArithmetic)]
    [InlineData(Rejection.ObjectTypeMismatch)]
    public void RejectsUnsafeOrUnprofitableCandidates(Rejection rejection)
    {
        var fixture = new Fixture(
            references: rejection == Rejection.ReferenceBearing);
        var parameters = rejection == Rejection.MultipleRoots
            ? new[] { fixture.ByRefAggregate, fixture.ByRefAggregate }
            : [fixture.ByRefAggregate];
        var steps = rejection switch
        {
            Rejection.InsufficientTraffic => fixture.DirectLoads(1).ToArray(),
            Rejection.UnknownCall => fixture.DirectLoads(4).Concat(
                Fixture.ScalarCall(20, fixture.Unknown)).ToArray(),
            Rejection.PointerArithmetic => fixture.DirectLoads(4).Concat(
                fixture.PointerArithmetic(20)).ToArray(),
            Rejection.ObjectTypeMismatch => fixture.DirectLoads(4).Concat(
                fixture.FieldAddressObjectLoad(20, CliValueKind.I8)).ToArray(),
            _ => fixture.DirectLoads(4).ToArray(),
        };
        var method = Fixture.Method(parameters, steps);
        if (rejection == Rejection.ExceptionRegion)
        {
            var group = new StructuredExceptionGroupId(0);
            method = method with
            {
                ExceptionGroups = method.ExceptionGroups.Add(
                    group,
                    new(group, null, 0, 30, [], [], [], null, null)),
            };
        }

        var plan = fixture.Plan(
            method,
            enabled: rejection != Rejection.Disabled);

        Assert.Empty(plan.PromotedParameters);
        Assert.Empty(plan.Projections);
    }

    [Fact]
    public void ValidatesDependenciesAndRequestMembers()
    {
        var fixture = new Fixture();
        var method = Fixture.Method(
            [fixture.ByRefAggregate],
            fixture.DirectLoads(4).ToArray());
        var request = fixture.Request(method, enabled: true);

        Assert.Throws<ArgumentNullException>(() => new ManagedByRefAggregatePromotionPlanner(
            null!, fixture, fixture, fixture, fixture, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new ManagedByRefAggregatePromotionPlanner(
            fixture, null!, fixture, fixture, fixture, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new ManagedByRefAggregatePromotionPlanner(
            fixture, fixture, null!, fixture, fixture, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new ManagedByRefAggregatePromotionPlanner(
            fixture, fixture, fixture, null!, fixture, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new ManagedByRefAggregatePromotionPlanner(
            fixture, fixture, fixture, fixture, null!, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new ManagedByRefAggregatePromotionPlanner(
            fixture, fixture, fixture, fixture, fixture, null!, fixture));
        Assert.Throws<ArgumentNullException>(() => new ManagedByRefAggregatePromotionPlanner(
            fixture, fixture, fixture, fixture, fixture, fixture, null!));

        var planner = fixture.CreatePlanner();
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { Method = null! }));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(request with { CallSites = null! }));
    }

    public enum Rejection
    {
        Disabled,
        InsufficientTraffic,
        MultipleRoots,
        ReferenceBearing,
        ExceptionRegion,
        UnknownCall,
        PointerArithmetic,
        ObjectTypeMismatch,
    }

    private static InstructionStep Step(
        CilInstruction instruction,
        params CliValueKind[] entryStack) => new(instruction, [.. entryStack]);

    private readonly record struct InstructionStep(
        CilInstruction Instruction,
        ImmutableArray<CliValueKind> EntryStack);

    private sealed class Fixture :
        IFieldRepository,
        ITypeDefinitionResolver,
        IValueLayoutProvider,
        IInstanceFieldLayoutProvider,
        IArgumentSignatureTypeResolver,
        ICilTypeOperandResolver,
        IRuntimeIntrinsicRegistry
    {
        private static readonly AssemblyIdentity Assembly = new("byref-promotion-tests");
        private static readonly EntityKey TypeKey = new(Assembly, 0x02000001);
        private static readonly CliTypeIdentity Container =
            CliTypeIdentity.Named(Assembly, "Tests", "Container", false);
        private readonly bool _references;
        private readonly FieldDefinitionModel _i4Field;
        private readonly FieldDefinitionModel _i8Field;

        public Fixture(bool references = false)
        {
            _references = references;
            Aggregate = CliTypeIdentity.Named(
                Assembly,
                "Tests",
                "Buffer",
                isValueType: true);
            ByRefAggregate = CliTypeIdentity.ManagedByReference(Aggregate);
            _i4Field = new(
                new(Assembly, 0x04000001),
                TypeKey,
                "Low",
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                false);
            _i8Field = new(
                new(Assembly, 0x04000002),
                TypeKey,
                "High",
                CliTypeIdentity.FromStackKind(CliValueKind.I8),
                false);
            Field32 = new(_i4Field, Aggregate, _i4Field.SignatureType);
            Field64 = new(_i8Field, Aggregate, _i8Field.SignatureType);
            Caller = new("Tests.Body");
            DivRem = Target(2, "DivRem", CliValueKind.I4);
            Unknown = Target(3, "Unknown", CliValueKind.I4);
        }

        public CliTypeIdentity Aggregate { get; }
        public CliTypeIdentity ByRefAggregate { get; }
        public FieldInstanceModel Field32 { get; }
        public FieldInstanceModel Field64 { get; }
        public ManagedMethodIdentity Caller { get; }
        public MethodInstanceModel DivRem { get; }
        public MethodInstanceModel Unknown { get; }
        public int AggregateSize { get; init; } = 12;
        public int AggregateAlignment { get; init; } = 4;
        public bool AggregateByReferences { get; init; }
        public int FieldOffset { get; init; }
        public int FieldSize { get; init; } = 4;
        public int ScalarSize { get; init; } = 4;
        public FieldDefinitionModel? FieldOverride { get; set; }

        public IEnumerable<InstructionStep> DirectLoads(int count, int offset = 0)
        {
            for (var index = 0; index < count; index++)
            {
                var start = offset + index * 3;
                var field = index % 2 == 0 ? Field32 : Field64;
                yield return Step(Instruction(
                    start,
                    CilOperation.LoadArgument,
                    new CilOperand.Index(0)));
                yield return Step(
                    Instruction(
                        start + 1,
                        CilOperation.LoadField,
                        new CilOperand.FieldInstance(field)),
                    CliValueKind.ManagedAddress);
                yield return Step(
                    Instruction(start + 2, CilOperation.Pop),
                    field.FieldType.StackKind);
            }
        }

        public IEnumerable<InstructionStep> FieldAddressStoreAcrossCall(
            int offset,
            MethodInstanceModel target)
        {
            yield return Step(Instruction(
                offset,
                CilOperation.LoadArgument,
                new CilOperand.Index(0)));
            yield return Step(
                Instruction(
                    offset + 1,
                    CilOperation.LoadFieldAddress,
                    new CilOperand.FieldInstance(Field32)),
                CliValueKind.ManagedAddress);
            yield return Step(
                Instruction(offset + 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
                CliValueKind.ManagedAddress);
            yield return Step(
                Instruction(offset + 3, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
                CliValueKind.ManagedAddress,
                CliValueKind.I4);
            yield return Step(
                Call(offset + 4, target),
                CliValueKind.ManagedAddress,
                CliValueKind.I4,
                CliValueKind.I4);
            yield return Step(
                Instruction(
                    offset + 5,
                    CilOperation.StoreObject,
                    new CilOperand.TypeIdentity(
                        CliTypeIdentity.FromStackKind(CliValueKind.I4))),
                CliValueKind.ManagedAddress,
                CliValueKind.I4);
        }

        public static IEnumerable<InstructionStep> ScalarCall(
            int offset,
            MethodInstanceModel target)
        {
            yield return Step(Instruction(
                offset,
                CilOperation.LoadInt32,
                new CilOperand.ConstantI4(3)));
            yield return Step(
                Instruction(offset + 1, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
                CliValueKind.I4);
            yield return Step(
                Call(offset + 2, target),
                CliValueKind.I4,
                CliValueKind.I4);
            yield return Step(
                Instruction(offset + 3, CilOperation.Pop),
                CliValueKind.I4);
        }

        public IEnumerable<InstructionStep> PointerArithmetic(int offset)
        {
            yield return Step(Instruction(
                offset,
                CilOperation.LoadArgument,
                new CilOperand.Index(0)));
            yield return Step(
                Instruction(
                    offset + 1,
                    CilOperation.LoadFieldAddress,
                    new CilOperand.FieldInstance(Field32)),
                CliValueKind.ManagedAddress);
            yield return Step(
                Instruction(offset + 2, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)),
                CliValueKind.ManagedAddress);
            yield return Step(
                Instruction(offset + 3, CilOperation.Add),
                CliValueKind.ManagedAddress,
                CliValueKind.I4);
            yield return Step(
                Instruction(offset + 4, CilOperation.Pop),
                CliValueKind.ManagedAddress);
        }

        public IEnumerable<InstructionStep> FieldAddressObjectLoad(
            int offset,
            CliValueKind loadedKind)
        {
            yield return Step(Instruction(
                offset,
                CilOperation.LoadArgument,
                new CilOperand.Index(0)));
            yield return Step(
                Instruction(
                    offset + 1,
                    CilOperation.LoadFieldAddress,
                    new CilOperand.FieldInstance(Field32)),
                CliValueKind.ManagedAddress);
            yield return Step(
                Instruction(
                    offset + 2,
                    CilOperation.LoadObject,
                    new CilOperand.TypeIdentity(
                        CliTypeIdentity.FromStackKind(loadedKind))),
                CliValueKind.ManagedAddress);
            yield return Step(
                Instruction(offset + 3, CilOperation.Pop),
                loadedKind);
        }

        public static StructuredMethod Method(
            IReadOnlyList<CliTypeIdentity> parameters,
            params InstructionStep[] steps)
        {
            var signature = new MethodSignatureModel(
                CliTypeIdentity.FromStackKind(CliValueKind.Void),
                [.. parameters]);
            var body = Definition(1, "Body", signature);
            var instructions = steps.Select(step => step.Instruction).ToImmutableArray();
            var blockId = new StructuredBlockId(0);
            var block = new StructuredBlockDefinition(
                blockId,
                instructions[0].Offset,
                instructions,
                steps[0].EntryStack,
                new StructuredFallthroughExit(null))
            {
                EndOffset = instructions[^1].NextOffset,
            };
            return new(
                new StructuredMethodHeader(body, null, 8, [], [], instructions),
                blockId,
                ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                    .Add(blockId, block),
                new StructuredSequence([
                    new StructuredCode(new(blockId, StructuredBlockRole.Owner)),
                ]),
                [],
                ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty,
                steps.ToImmutableDictionary(
                    step => step.Instruction.Offset,
                    step => step.EntryStack));
        }

        public ManagedByRefAggregatePromotionPlan Plan(
            StructuredMethod method,
            bool enabled = true) => CreatePlanner().Plan(Request(method, enabled));

        public ManagedByRefAggregatePromotionRequest Request(
            StructuredMethod method,
            bool enabled)
        {
            var calls = method.Header.Instructions
                .Where(instruction => instruction.Operation == CilOperation.Call)
                .Select(instruction =>
                {
                    var target = ((CilOperand.MethodInstance)instruction.Operand).Value;
                    return new ManagedCallSite(
                        new(Caller, instruction.Offset),
                        ManagedCallOperation.Direct,
                        new(target.CanonicalName),
                        target,
                        null);
                })
                .ToImmutableDictionary(call => call.Key);
            return new(method, Caller, Lookups.Of(calls), enabled);
        }

        public ManagedByRefAggregatePromotionPlanner CreatePlanner() =>
            new(this, this, this, this, this, this, this);

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => new(
            TypeKey,
            "Tests",
            "Buffer",
            IsValueType: true,
            [_i4Field.Key, _i8Field.Key],
            []);

        public ValueLayout GetValueLayout(CliTypeIdentity type) => type.StackKind switch
        {
            CliValueKind.I8 or CliValueKind.F8 => new(type, 8, 8, []),
            CliValueKind.I4 or CliValueKind.F4 => new(type, ScalarSize, 4, []),
            _ => new(type, AggregateSize, AggregateAlignment, _references ? [0] : [])
            {
                ByReferenceOffsets = AggregateByReferences ? [0] : [],
            },
        };

        public FieldDefinitionModel GetField(EntityKey key) =>
            key == _i4Field.Key ? FieldOverride ?? _i4Field : _i8Field;

        public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
            GetFieldLayout(field.Definition.Key);

        public FieldLayout GetFieldLayout(EntityKey field) =>
            field == _i4Field.Key
                ? new(FieldOffset) { Size = FieldSize, Type = _i4Field.SignatureType }
                : new(4) { Size = 8, Type = _i8Field.SignatureType };

        public CliTypeIdentity Resolve(StructuredMethodHeader header, int index)
        {
            var signature = header.MethodInstance?.Signature ?? header.Method.Signature;
            return signature.ParameterSignatureTypes[index];
        }

        public CliTypeIdentity Resolve(
            CilInstruction instruction,
            MethodInstanceModel? methodInstance) =>
            ((CilOperand.TypeIdentity)instruction.Operand).Value;

        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = RuntimeIntrinsic.IntegerDivRem;
            return method == DivRem.Definition.Key;
        }

        private static MethodInstanceModel Target(
            int token,
            string name,
            CliValueKind returnType)
        {
            var signature = new MethodSignatureModel(
                CliTypeIdentity.FromStackKind(returnType),
                [
                    CliTypeIdentity.FromStackKind(CliValueKind.I4),
                    CliTypeIdentity.FromStackKind(CliValueKind.I4),
                ]);
            var definition = Definition(token, name, signature);
            return new(definition, Container, [], signature);
        }

        private static CilInstruction Call(int offset, MethodInstanceModel target) =>
            Instruction(offset, CilOperation.Call, new CilOperand.MethodInstance(target));

        public static CilInstruction Instruction(
            int offset,
            CilOperation operation,
            CilOperand? operand = null) => new(
                offset,
                offset + 1,
                operation,
                operand ?? new CilOperand.None());

        private static MethodDefinitionModel Definition(
            int token,
            string name,
            MethodSignatureModel signature) => new(
                new(Assembly, 0x06000000 + token),
                TypeKey,
                name,
                true,
                signature,
                1);
    }
}
