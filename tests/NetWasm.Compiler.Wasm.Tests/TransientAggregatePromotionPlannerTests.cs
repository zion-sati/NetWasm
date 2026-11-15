// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class TransientAggregatePromotionPlannerTests
{
    [Theory]
    [InlineData("i1")]
    [InlineData("u1")]
    [InlineData("i2")]
    [InlineData("u2")]
    [InlineData("char")]
    [InlineData("bool")]
    public void NarrowIntegralResultsRetainTheirStorageNormalization(string primitive)
    {
        var fixture = new Fixture(primitive: primitive);

        var plan = fixture.Planner.Create(DirectMethod(fixture, fixture.Call(0)));

        Assert.Empty(plan.Producers);
        Assert.Empty(plan.Projections);
    }

    [Theory]
    [InlineData("i4", 4)]
    [InlineData("u4", 4)]
    [InlineData("i8", 8)]
    [InlineData("u8", 8)]
    [InlineData("nativeint", 4)]
    [InlineData("nativeint", 8)]
    [InlineData("nativeuint", 4)]
    [InlineData("nativeuint", 8)]
    public void PromotesSupportedIntegralWidths(string primitive, int size)
    {
        var fixture = new Fixture(primitive: primitive) { ElementSize = size };

        var plan = fixture.Planner.Create(DirectMethod(fixture, fixture.Call(0)));

        Assert.Equal(fixture.Element, plan.Producers[0]);
        Assert.Equal(new TransientAggregateProjection(0, 0), plan.Projections[6]);
        Assert.Equal(new TransientAggregateProjection(0, 1), plan.Projections[12]);
    }

    [Fact]
    public void ExceptionRegionsPreventTransientPromotion()
    {
        var fixture = new Fixture();
        var method = DirectMethod(fixture, fixture.Call(0));
        var group = new StructuredExceptionGroupId(0);
        method = method with
        {
            ExceptionGroups = method.ExceptionGroups.Add(group,
                new(group, null, 0, 13, [], [], [], null, null)),
        };

        Assert.Same(TransientAggregatePromotionPlan.Empty, fixture.Planner.Create(method));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(8, false)]
    public void FieldProjectionRequiresExactIdentityAndStorage(int shape, bool accepted)
    {
        var other = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var fixture = new Fixture
        {
            FirstFieldLayout = shape switch
            {
                3 => new FieldLayout(1) { Size = 8 },
                4 => new FieldLayout(0) { Size = 4 },
                5 => new FieldLayout(0) { Size = 8 },
                6 => new FieldLayout(0) { Size = 8, Type = other },
                _ => null,
            },
        };
        var field = fixture.Item1;
        field = shape switch
        {
            0 => field with { DeclaringType = other },
            1 => field with { Definition = field.Definition with { Name = "Other" } },
            2 => field with { FieldType = other },
            _ => field,
        };
        var projection = Instruction(6,
            shape == 8 ? CilOperation.LoadFieldAddress : CilOperation.LoadField,
            shape == 7 ? new CilOperand.Entity(field.Definition.Key) : new CilOperand.FieldInstance(field));
        var plan = fixture.Planner.Create(DirectMethod(fixture, fixture.Call(0), projection));

        if (accepted)
        {
            Assert.Equal(fixture.Element, plan.Producers[0]);
            Assert.Equal(new TransientAggregateProjection(0, 0), plan.Projections[6]);
            Assert.Equal(new TransientAggregateProjection(0, 1), plan.Projections[12]);
        }
        else
        {
            Assert.Empty(plan.Producers);
            Assert.Empty(plan.Projections);
        }
    }

    [Fact]
    public void RejectsMissingDependenciesAndMethod()
    {
        var fixture = new Fixture();
        Assert.Throws<ArgumentNullException>(() => new TransientAggregatePromotionPlanner(null!, fixture, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new TransientAggregatePromotionPlanner(fixture, null!, fixture, fixture));
        Assert.Throws<ArgumentNullException>(() => new TransientAggregatePromotionPlanner(fixture, fixture, null!, fixture));
        Assert.Throws<ArgumentNullException>(() => new TransientAggregatePromotionPlanner(fixture, fixture, fixture, null!));
        Assert.Throws<ArgumentNullException>(() => fixture.Planner.Create(null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void RejectsUnsupportedProducerSignaturesAndTupleIdentity(int shape)
    {
        var fixture = new Fixture();
        var other = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var element = shape == 9 ? CliTypeIdentity.Primitive("r8", CliValueKind.F8) : fixture.Element;
        var tupleDefinition = CliTypeIdentity.Named(shape == 8 ? new("other") : Assembly,
            "System", shape == 7 ? "OtherTuple`2" : "ValueTuple`2", true);
        var tuple = CliTypeIdentity.GenericInstantiation(tupleDefinition,
            shape == 4 ? [element] : shape == 5 ? [other, element] :
            shape == 6 ? [element, other] : [element, element]);
        var signature = MethodSignatureModel.Create(tuple,
            shape == 2 ? [element] : shape == 3 ? [element, other] : [element, element]);
        var call = fixture.Call(0) with
        {
            Operand = shape switch
            {
                0 => new CilOperand.None(),
                1 => new CilOperand.Entity(new(Assembly, 0x06000002)),
                _ => new CilOperand.MethodInstance(fixture.DivRem with { Signature = signature }),
            },
        };

        Assert.Empty(fixture.Planner.Create(DirectMethod(fixture, call)).Producers);
    }

    [Fact]
    public void ResolvesDefinitionOperandsAndRequiresTheDivRemIntrinsic()
    {
        var fixture = new Fixture();
        var call = fixture.Call(0) with { Operand = new CilOperand.Entity(fixture.DivRem.Definition.Key) };

        Assert.Equal(fixture.Element, fixture.Planner.Create(DirectMethod(fixture, call)).Producers[0]);

        var otherIntrinsic = new Fixture { Intrinsic = RuntimeIntrinsic.IntegerPopulationCount };
        Assert.Empty(otherIntrinsic.Planner.Create(DirectMethod(otherIntrinsic, otherIntrinsic.Call(0))).Producers);
    }

    [Fact]
    public void RejectsNoncontiguousTupleStorage()
    {
        var fixture = new Fixture { TupleSize = 24 };

        Assert.Empty(fixture.Planner.Create(DirectMethod(fixture, fixture.Call(0))).Producers);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(0, true)]
    public void TupleTransferRequiresAnExistingLocalOfTheExactType(int index, bool wrongType)
    {
        var fixture = new Fixture();
        var method = fixture.Method([wrongType ? fixture.Element : fixture.Tuple],
            fixture.Call(0), Instruction(5, CilOperation.StoreLocal, new CilOperand.Index(index)));

        Assert.Empty(fixture.Planner.Create(method).Producers);
    }

    [Fact]
    public void LocalPromotionRequiresBothTupleFields()
    {
        var fixture = new Fixture();
        var method = fixture.Method([fixture.Tuple], fixture.Call(0),
            Instruction(5, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(6, CilOperation.LoadLocal, new CilOperand.Index(0)),
            fixture.Field(7, fixture.Item1));

        Assert.Empty(fixture.Planner.Create(method).Producers);
    }

    private static StructuredMethod DirectMethod(Fixture fixture, CilInstruction call, CilInstruction? first = null) =>
        fixture.Method([fixture.Element], call,
            Instruction(5, CilOperation.Duplicate),
            first ?? fixture.Field(6, fixture.Item1),
            Instruction(11, CilOperation.StoreLocal, new CilOperand.Index(0)),
            fixture.Field(12, fixture.Item2));

    [Fact]
    public void PromotesDirectDuplicatedDivRemProjections()
    {
        var fixture = new Fixture();
        var method = fixture.Method(
            [CliTypeIdentity.FromStackKind(CliValueKind.I8)],
            fixture.Call(0),
            Instruction(5, CilOperation.Duplicate),
            fixture.Field(6, fixture.Item1),
            Instruction(11, CilOperation.StoreLocal, new CilOperand.Index(0)),
            fixture.Field(12, fixture.Item2));

        var plan = fixture.Planner.Create(method);

        Assert.Equal(fixture.Element, plan.Producers[0]);
        Assert.Equal(new TransientAggregateProjection(0, 0), plan.Projections[6]);
        Assert.Equal(new TransientAggregateProjection(0, 1), plan.Projections[12]);
        Assert.Empty(plan.LocalTransferOffsets);
        Assert.Empty(plan.PromotedLocalIndices);
    }

    [Fact]
    public void PromotesSingleBlockTupleLocalWhoseOnlyUsesAreFieldProjections()
    {
        var fixture = new Fixture();
        var method = fixture.Method(
            [fixture.Tuple],
            fixture.Call(0),
            Instruction(5, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(6, CilOperation.LoadLocal, new CilOperand.Index(0)),
            fixture.Field(7, fixture.Item1),
            Instruction(12, CilOperation.Pop),
            Instruction(13, CilOperation.LoadLocal, new CilOperand.Index(0)),
            fixture.Field(14, fixture.Item2));

        var plan = fixture.Planner.Create(method);

        Assert.Equal(fixture.Element, plan.Producers[0]);
        Assert.Equal([5, 6, 13], plan.LocalTransferOffsets.Order().ToArray());
        Assert.Equal([0], plan.PromotedLocalIndices);
        Assert.Equal(new TransientAggregateProjection(0, 0), plan.Projections[7]);
        Assert.Equal(new TransientAggregateProjection(0, 1), plan.Projections[14]);
    }

    [Fact]
    public void PromotesImmediateFieldProjectionsThroughTupleLocalAddresses()
    {
        var fixture = new Fixture();
        var method = fixture.Method(
            [fixture.Tuple],
            fixture.Call(0),
            Instruction(5, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(6, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            fixture.Field(7, fixture.Item1),
            Instruction(12, CilOperation.Pop),
            Instruction(13, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            fixture.Field(14, fixture.Item2));

        var plan = fixture.Planner.Create(method);

        Assert.Equal(fixture.Element, plan.Producers[0]);
        Assert.Equal([5, 6, 13], plan.LocalTransferOffsets.Order().ToArray());
        Assert.Equal([0], plan.PromotedLocalIndices);
        Assert.Equal(new TransientAggregateProjection(0, 0), plan.Projections[7]);
        Assert.Equal(new TransientAggregateProjection(0, 1), plan.Projections[14]);
    }

    [Fact]
    public void RejectsTupleLocalWhenItsAddressEscapes()
    {
        var fixture = new Fixture();
        var method = fixture.Method(
            [fixture.Tuple],
            fixture.Call(0),
            Instruction(5, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(6, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            Instruction(7, CilOperation.Pop),
            Instruction(8, CilOperation.LoadLocal, new CilOperand.Index(0)),
            fixture.Field(9, fixture.Item1),
            Instruction(14, CilOperation.LoadLocal, new CilOperand.Index(0)),
            fixture.Field(15, fixture.Item2));

        Assert.Empty(fixture.Planner.Create(method).Producers);
    }

    [Fact]
    public void RejectsUnknownTupleConsumer()
    {
        var fixture = new Fixture();
        var method = fixture.Method(
            [],
            fixture.Call(0),
            Instruction(5, CilOperation.Pop));

        Assert.Empty(fixture.Planner.Create(method).Producers);
    }

    [Fact]
    public void RejectsReferenceBearingTupleLayout()
    {
        var fixture = new Fixture(referenceOffsets: [0]);
        var method = fixture.Method(
            [CliTypeIdentity.FromStackKind(CliValueKind.I8)],
            fixture.Call(0),
            Instruction(5, CilOperation.Duplicate),
            fixture.Field(6, fixture.Item1),
            Instruction(11, CilOperation.StoreLocal, new CilOperand.Index(0)),
            fixture.Field(12, fixture.Item2));

        Assert.Empty(fixture.Planner.Create(method).Producers);
    }

    [Fact]
    public void RejectsByReferenceBearingTupleLayout()
    {
        var fixture = new Fixture(byReferenceOffsets: [0]);
        var method = fixture.Method(
            [CliTypeIdentity.FromStackKind(CliValueKind.I8)],
            fixture.Call(0),
            Instruction(5, CilOperation.Duplicate),
            fixture.Field(6, fixture.Item1),
            Instruction(11, CilOperation.StoreLocal, new CilOperand.Index(0)),
            fixture.Field(12, fixture.Item2));

        Assert.Empty(fixture.Planner.Create(method).Producers);
    }

    [Fact]
    public void RejectsTupleLocalWithAnotherDefinition()
    {
        var fixture = new Fixture();
        var method = fixture.Method(
            [fixture.Tuple],
            fixture.Call(0),
            Instruction(5, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(6, CilOperation.LoadLocal, new CilOperand.Index(0)),
            fixture.Field(7, fixture.Item1),
            fixture.Call(12),
            Instruction(17, CilOperation.StoreLocal, new CilOperand.Index(0)),
            Instruction(18, CilOperation.LoadLocal, new CilOperand.Index(0)),
            fixture.Field(19, fixture.Item2));

        Assert.Empty(fixture.Planner.Create(method).Producers);
    }

    private static CilInstruction Instruction(
        int offset,
        CilOperation operation,
        CilOperand? operand = null) => new(
        offset,
        offset + 1,
        operation,
        operand ?? new CilOperand.None());

    private sealed class Fixture :
        IMethodRepository,
        IRuntimeIntrinsicRegistry,
        IValueLayoutProvider,
        IInstanceFieldLayoutProvider
    {
        private readonly MethodDefinitionModel _divRem;
        private readonly MethodDefinitionModel _body;
        private readonly ImmutableArray<int> _referenceOffsets;
        private readonly ImmutableArray<int> _byReferenceOffsets;

        public Fixture(
            ImmutableArray<int> referenceOffsets = default,
            ImmutableArray<int> byReferenceOffsets = default,
            string primitive = "u8")
        {
            _referenceOffsets = referenceOffsets.IsDefault ? [] : referenceOffsets;
            _byReferenceOffsets = byReferenceOffsets.IsDefault ? [] : byReferenceOffsets;
            Element = CliTypeIdentity.Primitive(primitive, primitive switch
            {
                "i1" or "u1" or "i2" or "u2" or "char" or "bool" or "i4" or "u4" => CliValueKind.I4,
                "nativeint" or "nativeuint" => CliValueKind.NativeInt,
                _ => CliValueKind.I8,
            });
            var tupleDefinition = CliTypeIdentity.Named(
                Assembly,
                "System",
                "ValueTuple`2",
                isValueType: true);
            Tuple = CliTypeIdentity.GenericInstantiation(
                tupleDefinition,
                [Element, Element]);
            var math = CliTypeIdentity.Named(Assembly, "System", "Math", false);
            var signature = MethodSignatureModel.Create(Tuple, Element, Element);
            _divRem = new(
                Key(0x06000001),
                Key(0x02000001),
                "DivRem",
                true,
                signature,
                1);
            DivRem = new(_divRem, math, [], signature);
            Item1 = Field("Item1", 0x04000001);
            Item2 = Field("Item2", 0x04000002);
            _body = new(
                Key(0x06000002),
                Key(0x02000002),
                "Body",
                true,
                MethodSignatureModel.Create(CliValueKind.Void),
                1);
            Planner = new TransientAggregatePromotionPlanner(this, this, this, this);
        }

        public CliTypeIdentity Element { get; }
        public CliTypeIdentity Tuple { get; }
        public MethodInstanceModel DivRem { get; }
        public FieldInstanceModel Item1 { get; }
        public FieldInstanceModel Item2 { get; }
        public TransientAggregatePromotionPlanner Planner { get; }
        public int? TupleSize { get; init; }
        public int ElementSize { get; init; } = 8;
        public RuntimeIntrinsic Intrinsic { get; init; } = RuntimeIntrinsic.IntegerDivRem;
        public FieldLayout? FirstFieldLayout { get; init; }

        public CilInstruction Call(int offset) => Instruction(
            offset,
            CilOperation.Call,
            new CilOperand.MethodInstance(DivRem));

        public CilInstruction Field(int offset, FieldInstanceModel field)
        {
            Assert.Equal(Tuple, field.DeclaringType);
            return Instruction(
                offset,
                CilOperation.LoadField,
                new CilOperand.FieldInstance(field));
        }

        public StructuredMethod Method(
            ImmutableArray<CliTypeIdentity> locals,
            params CilInstruction[] instructions)
        {
            var blockId = new StructuredBlockId(0);
            var body = instructions.ToImmutableArray();
            var block = new StructuredBlockDefinition(
                blockId,
                body[0].Offset,
                body,
                [],
                new StructuredFallthroughExit(null))
            {
                EndOffset = body[^1].NextOffset,
            };
            var header = new StructuredMethodHeader(
                _body,
                null,
                4,
                [.. locals.Select(local => local.StackKind)],
                locals,
                body);
            return new(
                header,
                blockId,
                ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition>.Empty
                    .Add(blockId, block),
                new StructuredSequence([
                    new StructuredCode(new(blockId, StructuredBlockRole.Owner)),
                ]),
                [],
                ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup>.Empty,
                body.ToImmutableDictionary(
                    instruction => instruction.Offset,
                    _ => ImmutableArray<CliValueKind>.Empty));
        }

        public MethodDefinitionModel GetMethod(EntityKey key) =>
            key == _divRem.Key ? _divRem : _body;

        public bool TryGetIntrinsic(EntityKey method, out RuntimeIntrinsic intrinsic)
        {
            intrinsic = Intrinsic;
            return method == _divRem.Key;
        }

        public ValueLayout GetValueLayout(CliTypeIdentity type) => new(
            type,
            type.Equals(Tuple) ? TupleSize ?? ElementSize * 2 : ElementSize,
            ElementSize,
            _referenceOffsets)
        {
            ByReferenceOffsets = _byReferenceOffsets,
        };

        public FieldLayout GetFieldLayout(EntityKey field) =>
            GetFieldLayout(field == Item1.Definition.Key ? Item1 : Item2);

        public FieldLayout GetFieldLayout(FieldInstanceModel field) =>
            field.Definition.Name == "Item1" && FirstFieldLayout is { } overridden
                ? overridden
                : new(field.Definition.Name == "Item1" ? 0 : ElementSize)
                {
                    Size = ElementSize,
                    Type = Element,
                };

        private FieldInstanceModel Field(string name, int token)
        {
            var definition = new FieldDefinitionModel(
                Key(token),
                Key(0x02000003),
                name,
                Element,
                false);
            return new(definition, Tuple, Element);
        }

        private static EntityKey Key(int token) => new(Assembly, token);
    }

    private static readonly AssemblyIdentity Assembly = new("promotion-tests");
}
