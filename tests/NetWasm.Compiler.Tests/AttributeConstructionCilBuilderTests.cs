// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeConstructionCilBuilderTests
{
    private static readonly AssemblyIdentity Assembly = new("Attributes");
    private static readonly CliTypeIdentity AttributeType = CliTypeIdentity.Named(
        Assembly, "Example", "MarkerAttribute", false);
    private static readonly CliTypeIdentity ObjectType = CliTypeIdentity.FromStackKind(
        CliValueKind.ManagedReference);
    private static readonly CliTypeIdentity IntType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
    private static readonly CliTypeIdentity LongType = CliTypeIdentity.FromStackKind(CliValueKind.I8);
    private static readonly CliTypeIdentity TypeType = CliTypeIdentity.Named(
        Assembly, "System", "Type", false);
    private static readonly CliTypeIdentity StringType = CliTypeIdentity.Primitive(
        "string", CliValueKind.ManagedReference, false);

    public static TheoryData<AttributeArgumentPlan, CilOperation, CilOperand> Scalars => new()
    {
        { new(ObjectType, AttributeArgumentKind.Null), CilOperation.LoadNull, new CilOperand.None() },
        { new(IntType, AttributeArgumentKind.Boolean) { SignedValue = 1 },
            CilOperation.LoadInt32, new CilOperand.ConstantI4(1) },
        { new(IntType, AttributeArgumentKind.Character) { UnsignedValue = 'λ' },
            CilOperation.LoadInt32, new CilOperand.ConstantI4('λ') },
        { new(IntType, AttributeArgumentKind.SignedInteger) { SignedValue = -17 },
            CilOperation.LoadInt32, new CilOperand.ConstantI4(-17) },
        { new(IntType, AttributeArgumentKind.UnsignedInteger) { UnsignedValue = uint.MaxValue },
            CilOperation.LoadInt32, new CilOperand.ConstantI4(-1) },
        { new(LongType, AttributeArgumentKind.SignedInteger) { SignedValue = long.MinValue },
            CilOperation.LoadInt64, new CilOperand.ConstantI8(long.MinValue) },
        { new(LongType, AttributeArgumentKind.UnsignedInteger) { UnsignedValue = ulong.MaxValue },
            CilOperation.LoadInt64, new CilOperand.ConstantI8(-1) },
        { new(CliTypeIdentity.FromStackKind(CliValueKind.F4), AttributeArgumentKind.Floating32)
            { FloatingValue = 1.5 }, CilOperation.LoadFloat32, new CilOperand.ConstantF4(1.5f) },
        { new(CliTypeIdentity.FromStackKind(CliValueKind.F8), AttributeArgumentKind.Floating64)
            { FloatingValue = -2.5 }, CilOperation.LoadFloat64, new CilOperand.ConstantF8(-2.5) },
        { new(StringType, AttributeArgumentKind.Text) { StringValue = "payload" },
            CilOperation.LoadString, new CilOperand.UserString("payload") },
    };

    [Theory]
    [MemberData(nameof(Scalars))]
    public void BuildPreservesSerializedScalarValues(
        AttributeArgumentPlan argument, CilOperation operation, CilOperand operand)
    {
        var builder = new AttributeConstructionCilBuilder(new SetterBindings());
        var attribute = Create([argument]);

        var result = Build(builder, attribute, 3);

        Assert.Equal(operation, result.Instructions[0].Operation);
        Assert.Equal(operand, result.Instructions[0].Operand);
        Assert.Equal(new CilOperand.MethodInstance(attribute.Constructor),
            result.Instructions[1].Operand);
        Assert.Equal(CilOperation.NewObject, result.Instructions[1].Operation);
        Assert.Equal(AttributeType, Assert.Single(result.LocalTypes));
        Assert.Equal(new CilOperand.Index(3), result.Instructions[2].Operand);
        Assert.Equal(CilOperation.StoreLocal, result.Instructions[2].Operation);
        Assert.Equal(new CilOperand.Index(3), result.Instructions[3].Operand);
        Assert.Equal(CilOperation.LoadLocal, result.Instructions[3].Operation);
        Assert.Equal(1, result.MaxStack);
        Assert.Equal(Enumerable.Range(0, 4), result.Instructions.Select(item => item.Offset));
        Assert.Equal(Enumerable.Range(1, 4), result.Instructions.Select(item => item.NextOffset));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildBoxesTheConcreteSerializedTypeForObjectParameters(bool wide)
    {
        var builder = new AttributeConstructionCilBuilder(new SetterBindings());
        var serialized = wide ? LongType : IntType;
        var result = Build(builder, Create([
            new(ObjectType, AttributeArgumentKind.SignedInteger)
            {
                SerializedType = serialized,
                SignedValue = 42,
            },
        ]), 0);

        Assert.Equal(wide ? CilOperation.LoadInt64 : CilOperation.LoadInt32,
            result.Instructions[0].Operation);
        Assert.Equal(CilOperation.Box, result.Instructions[1].Operation);
        Assert.Equal(new CilOperand.TypeIdentity(serialized), result.Instructions[1].Operand);
        Assert.Equal(CilOperation.NewObject, result.Instructions[2].Operation);
    }

    [Fact]
    public void BuildMaterializesTypeArgumentsUsingTheirClosedIdentity()
    {
        var builder = new AttributeConstructionCilBuilder(new SetterBindings());
        var identity = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(Assembly, "Example", "Container`1", false), [IntType]);
        var result = Build(builder, Create([
            new(ObjectType, AttributeArgumentKind.Type)
            {
                SerializedType = TypeType,
                TypeValue = identity,
            },
        ]), 0);

        Assert.Equal(CilOperation.LoadTypeToken, result.Instructions[0].Operation);
        Assert.Equal(new CilOperand.TypeIdentity(identity), result.Instructions[0].Operand);
        Assert.Equal(CilOperation.MaterializeType, result.Instructions[1].Operation);
        Assert.DoesNotContain(result.Instructions, item => item.Operation == CilOperation.Box);
    }

    [Fact]
    public void BuildKeepsNestedArraysAndAttributeInstancesInTypedLocals()
    {
        var builder = new AttributeConstructionCilBuilder(new SetterBindings());
        var integers = CliTypeIdentity.SzArray(IntType);
        var objects = CliTypeIdentity.SzArray(ObjectType);
        var result = Build(builder, Create([
            new(objects, AttributeArgumentKind.Array)
            {
                Elements = [
                    new(ObjectType, AttributeArgumentKind.Array)
                    {
                        SerializedType = integers,
                        Elements = [new(IntType, AttributeArgumentKind.SignedInteger)
                        {
                            SignedValue = 7,
                        }],
                    },
                    new(ObjectType, AttributeArgumentKind.Null),
                ],
            },
        ]), 5);

        Assert.Equal<CliTypeIdentity>([objects, integers, AttributeType], result.LocalTypes);
        Assert.Equal([5, 6, 7], result.Instructions
            .Where(item => item.Operation == CilOperation.StoreLocal)
            .Select(item => ((CilOperand.Index)item.Operand).Value));
        Assert.Equal([IntType, ObjectType, ObjectType], result.Instructions
            .Where(item => item.Operation == CilOperation.StoreArrayElement)
            .Select(item => ((CilOperand.TypeIdentity)item.Operand).Value));
        Assert.Equal(5, result.MaxStack);
        Assert.Equal(new CilOperand.Index(7), result.Instructions[^1].Operand);
    }

    [Fact]
    public void BuildEmitsEmptyArraysWithoutInventingElements()
    {
        var builder = new AttributeConstructionCilBuilder(new SetterBindings());
        var result = Build(builder, Create([
            new(CliTypeIdentity.SzArray(IntType), AttributeArgumentKind.Array),
        ]), 0);

        Assert.Equal(new CilOperand.ConstantI4(0), result.Instructions[0].Operand);
        Assert.Single(result.Instructions, item => item.Operation == CilOperation.NewArray);
        Assert.DoesNotContain(result.Instructions, item => item.Operation == CilOperation.StoreArrayElement);
    }

    [Fact]
    public void BuildAppliesNamedMembersAfterConstructionInMetadataOrder()
    {
        var builder = new AttributeConstructionCilBuilder(new SetterBindings());
        var field = CreateField();
        var setter = CreateMethod("set_Label", [StringType]);
        var attribute = Create([]) with
        {
            NamedArguments = [
                new("Code", new(IntType, AttributeArgumentKind.SignedInteger) { SignedValue = 11 })
                    { Field = field },
                new("Label", new(StringType, AttributeArgumentKind.Text) { StringValue = "label" })
                    { Setter = setter },
            ],
        };

        var result = Build(builder, attribute, 2);

        Assert.Equal([
            CilOperation.NewObject, CilOperation.StoreLocal,
            CilOperation.LoadLocal, CilOperation.LoadInt32, CilOperation.StoreField,
            CilOperation.LoadLocal, CilOperation.LoadFunction, CilOperation.NewObject,
            CilOperation.LoadString, CilOperation.LoadString, CilOperation.Call,
            CilOperation.LoadLocal,
        ], result.Instructions.Select(item => item.Operation));
        Assert.Equal(new CilOperand.FieldInstance(field), result.Instructions[4].Operand);
        Assert.Equal(new CilOperand.MethodInstance(setter), result.Instructions[6].Operand);
        Assert.Equal(new CilOperand.MethodInstance(SetterBindings.Constructor), result.Instructions[7].Operand);
        Assert.Equal(new CilOperand.UserString("Label"), result.Instructions[9].Operand);
        Assert.Equal(new CilOperand.MethodInstance(SetterBindings.Invoke), result.Instructions[10].Operand);
        Assert.Equal(3, result.MaxStack);
    }

    [Fact]
    public void VirtualPropertySettersUseVirtualDelegateBinding()
    {
        var setter = CreateMethod("set_Label", [StringType]);
        setter = setter with { Definition = setter.Definition with { IsVirtual = true } };
        var attribute = Create([]) with
        {
            NamedArguments = [new("Label", new(StringType, AttributeArgumentKind.Text) { StringValue = "label" })
                { Setter = setter }],
        };
        var result = Build(new AttributeConstructionCilBuilder(new SetterBindings()), attribute, 0);

        Assert.Equal([
            CilOperation.NewObject, CilOperation.StoreLocal, CilOperation.LoadLocal,
            CilOperation.Duplicate, CilOperation.LoadVirtualFunction, CilOperation.NewObject,
            CilOperation.LoadString, CilOperation.LoadString, CilOperation.Call, CilOperation.LoadLocal,
        ], result.Instructions.Select(item => item.Operation));
        Assert.Equal(new CilOperand.MethodInstance(setter), result.Instructions[4].Operand);
        Assert.Equal(new CilOperand.MethodInstance(SetterBindings.Invoke), result.Instructions[8].Operand);
        Assert.Equal(3, result.MaxStack);
    }

    [Theory]
    [InlineData(AttributeArgumentKind.Text)]
    [InlineData(AttributeArgumentKind.Type)]
    [InlineData(AttributeArgumentKind.Array)]
    [InlineData((AttributeArgumentKind)999)]
    public void BuildRejectsIncompleteArgumentPlans(AttributeArgumentKind kind)
    {
        var builder = new AttributeConstructionCilBuilder(new SetterBindings());
        Assert.Throws<CompilerException>(() => Build(builder, Create([new(ObjectType, kind)]), 0));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BuildRejectsMissingOrAmbiguousNamedMembers(bool both)
    {
        var builder = new AttributeConstructionCilBuilder(new SetterBindings());
        var attribute = Create([]) with
        {
            NamedArguments = [new("Code", new(IntType, AttributeArgumentKind.SignedInteger))
            {
                Field = both ? CreateField() : null,
                Setter = both ? CreateMethod("set_Code", [IntType]) : null,
            }],
        };

        Assert.Throws<CompilerException>(() => Build(builder, attribute, 0));
    }

    [Fact]
    public void BuildRejectsInvalidInputs()
    {
        var builder = new AttributeConstructionCilBuilder(new SetterBindings());
        Assert.Throws<ArgumentNullException>(() => Build(builder, null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(builder, Create([]), -1));
    }

    private static AttributeCilFragment Build<TBuilder>(
        TBuilder builder, AttributeConstructionPlan attribute, int firstLocal)
        where TBuilder : IAttributeConstructionCilBuilder => builder.Build(attribute, firstLocal);

    private static AttributeConstructionPlan Create(ImmutableArray<AttributeArgumentPlan> arguments) =>
        new(AttributeType, CreateMethod(".ctor", [.. arguments.Select(argument => argument.Type)]),
            arguments, []);

    private static FieldInstanceModel CreateField() => new(
        new(new(Assembly, 0x04000001), new(Assembly, 0x02000001), "Code", IntType, false),
        AttributeType, IntType);

    private static MethodInstanceModel CreateMethod(string name, ImmutableArray<CliTypeIdentity> parameters)
    {
        var signature = MethodSignatureModel.Create(CliTypeIdentity.FromStackKind(CliValueKind.Void),
            [.. parameters]);
        return new(new(new(Assembly, 0x06000001), new(Assembly, 0x02000001), name, false, signature, 1),
            AttributeType, [], signature);
    }

    private sealed class SetterBindings : IAttributeSetterBindingResolver
    {
        internal static readonly MethodInstanceModel Constructor = CreateMethod("delegate.ctor", [ObjectType, IntType]);
        internal static readonly MethodInstanceModel Invoke = CreateMethod("ApplySetter", [ObjectType, StringType, StringType]);

        public AttributeSetterBinding Resolve(CliTypeIdentity valueType)
        {
            Assert.Equal(StringType, valueType);
            return new(Constructor, Invoke);
        }
    }
}
