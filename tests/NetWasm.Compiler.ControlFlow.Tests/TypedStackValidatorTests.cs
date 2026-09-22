using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using Final = NetWasm.Compiler.ControlFlow.Structured;

namespace NetWasm.Compiler.ControlFlow.Tests;

using static NetWasm.Compiler.ControlFlow.Tests.ControlFlowTestSupport;

public sealed class TypedStackValidatorTests
{
    [Theory]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.NativeInt)]
    public void ValidatorAcceptsCliArrayLengthTypes(CliValueKind lengthType)
    {
        _ = Validate(Body(
            CliValueKind.Void,
            1,
            [lengthType],
            I(0, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(1, CilOperation.NewArray,
                new CilOperand.TypeIdentity(
                    CliTypeIdentity.Primitive("i4", CliValueKind.I4))),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return)));
    }

    [Fact]
    public void ValidatorRejectsNonCliArrayLengthType()
    {
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [CliValueKind.I8],
                I(0, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(1, CilOperation.NewArray,
                    new CilOperand.TypeIdentity(
                        CliTypeIdentity.Primitive("i4", CliValueKind.I4))),
                I(2, CilOperation.Pop),
                I(3, CilOperation.Return))),
            "array length must be int32 or native integer");
    }

    [Fact]
    public void ValidatorRequiresVirtualInstanceTargetForLoadVirtualFunction()
    {
        var valid = Body(
            CliValueKind.NativeInt,
            1,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.LoadVirtualFunction,
                new CilOperand.Entity(InstanceCallKey)),
            I(2, CilOperation.Return));

        _ = Validate(valid);

        var rankOneArray = CliTypeIdentity.Array(
            CliTypeIdentity.Primitive("primitive:i4", CliValueKind.I4),
            1);
        _ = Validate(Body(
            CliValueKind.ManagedAddress,
            2,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.Readonly),
            I(3, CilOperation.LoadRectangularArrayElementAddress,
                new CilOperand.TypeIdentity(rankOneArray)),
            I(4, CilOperation.Return)));

        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.NativeInt,
                1,
                [],
                I(0, CilOperation.LoadNull),
                I(1, CilOperation.LoadVirtualFunction,
                    new CilOperand.Entity(NonVirtualCallKey)),
                I(2, CilOperation.Return))),
            "ldvirtftn requires a virtual instance method");

        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.NativeInt,
                1,
                [],
                I(0, CilOperation.LoadNull),
                I(1, CilOperation.LoadVirtualFunction,
                    new CilOperand.Entity(StaticCallKey)),
                I(2, CilOperation.Return))),
            "ldvirtftn requires a virtual instance method");
    }

    [Fact]
    public void ValidatorAcceptsVolatileMemoryPrefixesAndRejectsInvalidPlacement()
    {
        var valid = Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Volatile),
            I(2, CilOperation.StoreStaticField, new CilOperand.Entity(StaticFieldKey)),
            I(3, CilOperation.Volatile),
            I(4, CilOperation.LoadStaticField, new CilOperand.Entity(StaticFieldKey)),
            I(5, CilOperation.Return));

        _ = Validate(valid);

        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.I4,
                1,
                [],
                I(0, CilOperation.Volatile),
                I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(2, CilOperation.Return))),
            "volatile. must immediately precede a supported memory access");
    }

    [Fact]
    public void ValidatorAcceptsReadonlyArrayAddressAndRejectsInvalidPlacement()
    {
        var valid = Body(
            CliValueKind.ManagedAddress,
            2,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.Readonly),
            I(3, CilOperation.LoadArrayElementAddress,
                new CilOperand.TypeIdentity(CliTypeIdentity.Primitive(
                    "primitive:i4", CliValueKind.I4))),
            I(4, CilOperation.Return));

        _ = Validate(valid);

        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.I4,
                1,
                [],
                I(0, CilOperation.Readonly),
                I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(2, CilOperation.Return))),
            "readonly. must immediately precede an array element address load");
    }

    [Fact]
    public void ValidatorAcceptsPortableCilPrefixesAndFiniteChecks()
    {
        var valid = Body(
            CliValueKind.I4,
            2,
            [],
            I(0, CilOperation.Break),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(2, CilOperation.ConvertFloatUnsigned),
            I(3, CilOperation.CheckFinite),
            I(4, CilOperation.ConvertNumeric, new CilOperand.NumericConversion(
                32, false, false, false, false)),
            I(5, CilOperation.LoadStaticFieldAddress,
                new CilOperand.Entity(StaticFieldKey)),
            I(6, CilOperation.Unaligned, new CilOperand.Index(1)),
            I(7, CilOperation.LoadObject, new CilOperand.TypeIdentity(
                CliTypeIdentity.Primitive("primitive:i4", CliValueKind.I4))),
            I(8, CilOperation.Add),
            I(9, CilOperation.Return));

        _ = Validate(valid);

        AssertDiagnostic(
            () => Validate(valid with
            {
                Instructions = valid.Instructions.SetItem(
                    6,
                    I(6, CilOperation.Unaligned, new CilOperand.Index(3))),
            }),
            "alignment must be 1, 2, or 4");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.I4,
                1,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.CheckFinite),
                I(2, CilOperation.Return))),
            "ckfinite requires a floating value");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.Unaligned, new CilOperand.Index(1)),
                I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(2, CilOperation.Return))),
            "unaligned. must immediately precede a supported memory access");
    }

    [Fact]
    public void ValidatorAcceptsWideAndFloatingPointConversions()
    {
        var body = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadInt64, new CilOperand.ConstantI8(42)),
            I(1, CilOperation.ConvertInt32),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadFloat32, new CilOperand.ConstantF4(1.25f)),
            I(4, CilOperation.ConvertInt64),
            I(5, CilOperation.Pop),
            I(6, CilOperation.LoadFloat64, new CilOperand.ConstantF8(2.5d)),
            I(7, CilOperation.ConvertFloat32),
            I(8, CilOperation.ConvertFloat64),
            I(9, CilOperation.Pop),
            I(10, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)),
            I(11, CilOperation.ConvertNativeInt),
            I(12, CilOperation.Pop),
            I(13, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)),
            I(14, CilOperation.ConvertNativeUInt),
            I(15, CilOperation.Pop),
            I(16, CilOperation.Return));

        _ = Validate(body);
    }

    [Fact]
    public void ValidatorAcceptsNumericKernelOperationsAndWideShiftCounts()
    {
        var body = Body(
            CliValueKind.Void,
            2,
            [],
            I(0, CilOperation.LoadInt64, new CilOperand.ConstantI8(42)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)),
            I(2, CilOperation.ShiftLeft),
            I(3, CilOperation.LoadInt64, new CilOperand.ConstantI8(3)),
            I(4, CilOperation.RemainderUnsigned),
            I(5, CilOperation.Negate),
            I(6, CilOperation.OnesComplement),
            I(7, CilOperation.ConvertNumeric,
                new CilOperand.NumericConversion(8, true, true, false, false)),
            I(8, CilOperation.Pop),
            I(9, CilOperation.Return));

        _ = Validate(body);
    }

    [Fact]
    public void ValidatorRejectsIncompatibleNumericAndComparisonOperands()
    {
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                2,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.LoadInt64, new CilOperand.ConstantI8(2)),
                I(2, CilOperation.Add),
                I(3, CilOperation.Pop),
                I(4, CilOperation.Return))),
            "numeric operands are incompatible");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                2,
                [],
                I(0, CilOperation.LoadFloat32, new CilOperand.ConstantF4(1)),
                I(1, CilOperation.LoadFloat32, new CilOperand.ConstantF4(2)),
                I(2, CilOperation.BitwiseOr),
                I(3, CilOperation.Pop),
                I(4, CilOperation.Return))),
            "bitwise operation requires an integer operand");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                2,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.LoadString, new CilOperand.UserString("x")),
                I(2, CilOperation.CompareEqual),
                I(3, CilOperation.Pop),
                I(4, CilOperation.Return))),
            "comparison operands are incompatible");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadString, new CilOperand.UserString("x")),
                I(1, CilOperation.ConvertInt32),
                I(2, CilOperation.Pop),
                I(3, CilOperation.Return))),
            "operation requires a numeric operand");
    }

    [Fact]
    public void ValidatorAcceptsFieldsCallsConstructionAndScalarOperations()
    {
        var program = new FakeProgram();
        var body = Body(
            CliValueKind.I4,
            4,
            [CliValueKind.I4, CliValueKind.ManagedReference],
            I(0, CilOperation.Nop),
            I(1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(2, CilOperation.StoreLocal, new CilOperand.Index(0)),
            I(3, CilOperation.LoadString, new CilOperand.UserString("x")),
            I(4, CilOperation.StoreLocal, new CilOperand.Index(1)),
            I(5, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(6, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(7, CilOperation.StoreField, new CilOperand.Entity(InstanceFieldKey)),
            I(8, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(9, CilOperation.LoadField, new CilOperand.Entity(InstanceFieldKey)),
            I(10, CilOperation.StoreStaticField, new CilOperand.Entity(StaticFieldKey)),
            I(11, CilOperation.LoadStaticField, new CilOperand.Entity(StaticFieldKey)),
            I(12, CilOperation.Call, new CilOperand.Entity(StaticCallKey)),
            I(13, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(14, CilOperation.NewObject, new CilOperand.Entity(ConstructorKey)),
            I(15, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(16, CilOperation.CallVirtual, new CilOperand.Entity(InstanceCallKey)),
            I(17, CilOperation.Add),
            I(18, CilOperation.Return));

        var graph = ControlFlowGraphBuilder.Build(body);
        var validated = CreateValidator(program).Validate(graph);

        Assert.Equal([], validated.EntryStacks[0]);
        Assert.True(validated.InstructionEntryStacks[7].AsSpan().SequenceEqual(
            [CliValueKind.ManagedReference, CliValueKind.I4]));
        Assert.True(validated.InstructionEntryStacks[17].AsSpan().SequenceEqual(
            [CliValueKind.I4, CliValueKind.I4]));
    }

    [Theory]
    [InlineData("IntPtr", false)]
    [InlineData("IntPtr", true)]
    [InlineData("UIntPtr", false)]
    [InlineData("UIntPtr", true)]
    public void ValidatorNormalizesConstructedNativeIntegerStackKinds(
        string typeName,
        bool useMethodInstance)
    {
        var program = new FakeProgram();
        var constructorKey = typeName == "IntPtr"
            ? IntPtrConstructorKey
            : UIntPtrConstructorKey;
        var constructor = program.GetMethod(constructorKey);
        CilOperand constructorOperand = useMethodInstance
            ? new CilOperand.MethodInstance(new MethodInstanceModel(
                constructor,
                CliTypeIdentity.Named(Assembly, "System", typeName, isValueType: true),
                [],
                constructor.Signature))
            : new CilOperand.Entity(constructorKey);
        var body = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(8)),
            I(1, CilOperation.NewObject, constructorOperand),
            I(2, CilOperation.Call, new CilOperand.Entity(NativeIntCallKey)),
            I(3, CilOperation.Return));

        var validated = CreateValidator(program).Validate(CreateGraphBuilder().Build(body));

        Assert.True(validated.InstructionEntryStacks[2].AsSpan().SequenceEqual(
            [CliValueKind.NativeInt]));
    }

    [Fact]
    public void ValidatorAcceptsIndirectCallsThroughTheirCallSiteContract()
    {
        var body = Body(
            CliValueKind.I4,
            2,
            [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.LoadFunction, new CilOperand.Entity(StaticCallKey)),
            I(2, CilOperation.CallIndirect, new CilOperand.CallSite(
                MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4))),
            I(3, CilOperation.Return));

        _ = Validate(body);

        AssertDiagnostic(
            () => Validate(body with
            {
                Instructions = body.Instructions.SetItem(
                    2,
                    I(2, CilOperation.CallIndirect)),
            }),
            "calli requires a call-site signature");
    }

    [Fact]
    public void ValidatorCoversInstanceArgumentsAndValueTypeCallPaths()
    {
        var instanceBody = Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.LoadArgument, new CilOperand.Index(1)),
            I(3, CilOperation.Return)) with
        {
            Method = Body(
                CliValueKind.I4,
                1,
                [],
                I(0, CilOperation.Return)).Method with
            {
                IsStatic = false,
            },
        };
        _ = Validate(instanceBody);

        var valueInstanceBody = instanceBody with
        {
            Method = instanceBody.Method with { DeclaringType = ValueTypeKey },
            Instructions =
            [
                I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
                I(1, CilOperation.Pop),
                I(2, CilOperation.LoadArgument, new CilOperand.Index(1)),
                I(3, CilOperation.Return),
            ],
        };
        _ = Validate(valueInstanceBody);

        var instanceDefinition = new MethodDefinitionModel(
            InstanceCallKey,
            TypeKey,
            "Instance",
            false,
            MethodSignatureModel.Create(CliValueKind.I4),
            0);
        var instance = new MethodInstanceModel(
            instanceDefinition,
            CliTypeIdentity.Named(Assembly, "Test", "Type", false),
            [],
            instanceDefinition.Signature);
        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Call, new CilOperand.MethodInstance(instance)),
            I(2, CilOperation.Return)));

        var valueInstance = instance with
        {
            DeclaringType = CliTypeIdentity.Named(Assembly, "Test", "Value", true),
        };
        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadStaticFieldAddress,
                new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.Call, new CilOperand.MethodInstance(valueInstance)),
            I(2, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadStaticFieldAddress,
                new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.Call, new CilOperand.Entity(ValueInstanceCallKey)),
            I(2, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.I4,
            2,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewObject, new CilOperand.Entity(ValueConstructorKey)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(4, CilOperation.Return)));
    }

    [Fact]
    public void ValidatorAcceptsAStaticCallAtTheMethodEntry()
    {
        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.Call, new CilOperand.Entity(NoArgumentStaticCallKey)),
            I(1, CilOperation.Return)));
    }

    [Fact]
    public void ValidatorAcceptsNativePointersAcrossManagedAddressConsumers()
    {
        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadStaticFieldAddress, new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.ConvertNativeInt),
            I(2, CilOperation.Call, new CilOperand.Entity(ManagedAddressCallKey)),
            I(3, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadStaticFieldAddress, new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.ConvertNativeInt),
            I(2, CilOperation.Call, new CilOperand.Entity(ValueInstanceCallKey)),
            I(3, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadStaticFieldAddress, new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.ConvertNativeInt),
            I(2, CilOperation.LoadField, new CilOperand.Entity(InstanceFieldKey)),
            I(3, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.ManagedAddress,
            1,
            [],
            I(0, CilOperation.LoadStaticFieldAddress, new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.ConvertNativeInt),
            I(2, CilOperation.LoadFieldAddress, new CilOperand.Entity(InstanceFieldKey)),
            I(3, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.I4,
            2,
            [],
            I(0, CilOperation.LoadStaticFieldAddress, new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.ConvertNativeInt),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
            I(3, CilOperation.StoreField, new CilOperand.Entity(InstanceFieldKey)),
            I(4, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(5, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.I4,
            2,
            [],
            I(0, CilOperation.LoadStaticFieldAddress, new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.LoadStaticFieldAddress, new CilOperand.Entity(StaticFieldKey)),
            I(2, CilOperation.ConvertNativeInt),
            I(3, CilOperation.CompareEqual),
            I(4, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.ManagedAddress,
            1,
            [],
            I(0, CilOperation.LoadStaticFieldAddress, new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.ConvertNativeInt),
            I(2, CilOperation.Return)));
    }

    [Fact]
    public void ValidatorChecksArrayDataAndNarrowNumericConversions()
    {
        _ = Validate(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.InitializeArrayData,
                new CilOperand.ByteData([1])),
            I(2, CilOperation.Return)));

        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadNull),
                I(1, CilOperation.InitializeArrayData,
                    new CilOperand.ByteData([])),
                I(2, CilOperation.Return))),
            "array initializer data is empty");

        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadNull),
                I(1, CilOperation.InitializeArrayData),
                I(2, CilOperation.Return))),
            "array initializer data is empty");

        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.ConvertNumeric,
                new CilOperand.NumericConversion(8, false, false, false, false)),
            I(2, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.NativeInt,
            1,
            [],
            I(0, CilOperation.LoadStaticFieldAddress,
                new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.ConvertNativeInt),
            I(2, CilOperation.Return)));

        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.NativeInt,
                1,
                [],
                I(0, CilOperation.LoadNull),
                I(1, CilOperation.ConvertNativeInt),
                I(2, CilOperation.Return))),
            "operation requires a numeric or managed-address operand");

        _ = Validate(Body(
            CliValueKind.I8,
            1,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.ConvertNumeric,
                new CilOperand.NumericConversion(64, false, false, false, false)),
            I(2, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.NativeInt,
            1,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.ConvertNumeric,
                new CilOperand.NumericConversion(32, false, false, false, true)),
            I(2, CilOperation.Return)));
    }

    [Fact]
    public void ValidatorAcceptsArgumentFieldTokenAndArrayReferenceOperations()
    {
        var field = new FieldInstanceModel(
            new FakeProgram().GetField(InstanceFieldKey),
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            CliTypeIdentity.Primitive("i4", CliValueKind.I4));

        _ = Validate(Body(
            CliValueKind.Void,
            3,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.StoreArgument, new CilOperand.Index(0)),
            I(2, CilOperation.LoadFieldToken, new CilOperand.Entity(InstanceFieldKey)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.LoadNull),
            I(5, CilOperation.LoadFieldAddress, new CilOperand.FieldInstance(field)),
            I(6, CilOperation.Pop),
            I(7, CilOperation.LoadNull),
            I(8, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(9, CilOperation.StoreField, new CilOperand.FieldInstance(field)),
            I(10, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.Void,
            3,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.LoadArrayElementReference,
                new CilOperand.TypeIdentity(CliTypeIdentity.Primitive("i4", CliValueKind.I4))),
            I(3, CilOperation.Pop),
            I(4, CilOperation.LoadNull),
            I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(6, CilOperation.LoadNull),
            I(7, CilOperation.StoreArrayElementReference),
            I(8, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.NewArray,
                new CilOperand.TypeIdentity(CliTypeIdentity.Primitive("i4", CliValueKind.I4))),
            I(2, CilOperation.LoadArrayLength),
            I(3, CilOperation.Pop),
            I(4, CilOperation.Return)));
    }

    [Fact]
    public void ValidatorAcceptsBoxingAndComparableBranchOperations()
    {
        _ = Validate(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.Box,
                new CilOperand.TypeIdentity(CliTypeIdentity.Primitive("i4", CliValueKind.I4))),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return)));

        foreach (var operation in new[]
        {
            CilOperation.BranchIfEqual,
            CilOperation.BranchIfNotEqual,
            CilOperation.BranchIfGreaterThanSigned,
            CilOperation.BranchIfGreaterThanUnsigned,
            CilOperation.BranchIfGreaterThanOrEqualSigned,
            CilOperation.BranchIfGreaterThanOrEqualUnsigned,
            CilOperation.BranchIfLessThanSigned,
            CilOperation.BranchIfLessThanUnsigned,
            CilOperation.BranchIfLessThanOrEqualSigned,
            CilOperation.BranchIfLessThanOrEqualUnsigned,
        })
        {
            _ = Validate(Body(
                CliValueKind.Void,
                2,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
                I(2, operation, new CilOperand.BranchTarget(4)),
                I(3, CilOperation.Return),
                I(4, CilOperation.Return)));
        }

        _ = Validate(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadFloat32, new CilOperand.ConstantF4(1)),
            I(1, CilOperation.ConvertNumeric,
                new CilOperand.NumericConversion(32, false, false, false, false)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return)));

        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                2,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.LoadInt64, new CilOperand.ConstantI8(2)),
                I(2, CilOperation.CompareEqual),
                I(3, CilOperation.Pop),
                I(4, CilOperation.Return))),
            "comparison operands are incompatible");

        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                2,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.NewObject, new CilOperand.Entity(ValueConstructorKey)),
                I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
                I(3, CilOperation.NewObject, new CilOperand.Entity(ValueConstructorKey)),
                I(4, CilOperation.CompareEqual),
                I(5, CilOperation.Pop),
                I(6, CilOperation.Return))),
            "comparison operands are incompatible");

        var method = new MethodDefinitionModel(
            Key(0x06000020),
            TypeKey,
            "InstanceBody",
            true,
            MethodSignatureModel.Create(CliValueKind.I4),
            1);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [],
            MethodSignatureModel.Create(CliValueKind.I4));
        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.Return)) with
        { MethodInstance = instance });

        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadArgument, new CilOperand.Index(0)),
            I(1, CilOperation.Return)) with
        {
            MethodInstance = instance with
            {
                Signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
            },
        });
    }

    [Theory]
    [InlineData(CilOperation.Add)]
    [InlineData(CilOperation.Subtract)]
    [InlineData(CilOperation.Multiply)]
    [InlineData(CilOperation.BitwiseOr)]
    [InlineData(CilOperation.BitwiseXor)]
    [InlineData(CilOperation.ShiftRightUnsigned)]
    [InlineData(CilOperation.AddChecked)]
    [InlineData(CilOperation.SubtractChecked)]
    [InlineData(CilOperation.MultiplyChecked)]
    [InlineData(CilOperation.MultiplyCheckedUnsigned)]
    [InlineData(CilOperation.Divide)]
    [InlineData(CilOperation.DivideUnsigned)]
    [InlineData(CilOperation.Remainder)]
    [InlineData(CilOperation.RemainderUnsigned)]
    [InlineData(CilOperation.BitwiseAnd)]
    [InlineData(CilOperation.ShiftLeft)]
    [InlineData(CilOperation.ShiftRightSigned)]
    [InlineData(CilOperation.CompareEqual)]
    [InlineData(CilOperation.CompareGreaterThanUnsigned)]
    [InlineData(CilOperation.CompareGreaterThanSigned)]
    [InlineData(CilOperation.CompareLessThanSigned)]
    [InlineData(CilOperation.CompareLessThanUnsigned)]
    public void ValidatorAcceptsEveryBinaryIntegerOperation(CilOperation operation)
    {
        var body = Body(
            CliValueKind.I4,
            2,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(2, operation),
            I(3, CilOperation.Return));

        _ = Validate(body);
    }

    [Theory]
    [InlineData(CilOperation.BitwiseAnd)]
    [InlineData(CilOperation.BitwiseOr)]
    [InlineData(CilOperation.BitwiseXor)]
    public void ValidatorAcceptsMixedNativeIntAndInt32BitwiseOperands(
        CilOperation operation)
    {
        var body = Body(
            CliValueKind.NativeInt,
            2,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)),
            I(1, CilOperation.ConvertNativeUInt),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(15)),
            I(3, operation),
            I(4, CilOperation.Return));

        _ = Validate(body);
    }

    [Fact]
    public void ValidatorAcceptsBothMixedNativeIntAndInt32NumericOperandOrders()
    {
        _ = Validate(Body(
            CliValueKind.NativeInt,
            2,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)),
            I(1, CilOperation.ConvertNativeInt),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(15)),
            I(3, CilOperation.Add),
            I(4, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.NativeInt,
            2,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(15)),
            I(2, CilOperation.ConvertNativeInt),
            I(3, CilOperation.Add),
            I(4, CilOperation.Return)));
    }

    [Theory]
    [InlineData(CilOperation.Add, CliValueKind.ManagedAddress, CliValueKind.I4,
        CliValueKind.ManagedAddress)]
    [InlineData(CilOperation.Add, CliValueKind.I4, CliValueKind.ManagedAddress,
        CliValueKind.ManagedAddress)]
    [InlineData(CilOperation.Add, CliValueKind.ManagedAddress, CliValueKind.NativeInt,
        CliValueKind.ManagedAddress)]
    [InlineData(CilOperation.Add, CliValueKind.NativeInt, CliValueKind.ManagedAddress,
        CliValueKind.ManagedAddress)]
    [InlineData(CilOperation.Subtract, CliValueKind.ManagedAddress, CliValueKind.I4,
        CliValueKind.ManagedAddress)]
    [InlineData(CilOperation.Subtract, CliValueKind.ManagedAddress, CliValueKind.NativeInt,
        CliValueKind.ManagedAddress)]
    [InlineData(CilOperation.Subtract, CliValueKind.ManagedAddress,
        CliValueKind.ManagedAddress, CliValueKind.NativeInt)]
    public void ValidatorAcceptsEveryCliManagedAddressArithmeticShape(
        CilOperation operation,
        CliValueKind left,
        CliValueKind right,
        CliValueKind result)
    {
        _ = Validate(Body(
            result,
            2,
            [],
            I(0, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(1, CilOperation.LoadLocal, new CilOperand.Index(1)),
            I(2, operation),
            I(3, CilOperation.Return)) with
        {
            Locals = [left, right],
        });
    }

    [Theory]
    [InlineData(CilOperation.Add, CliValueKind.ManagedAddress, CliValueKind.I8)]
    [InlineData(CilOperation.Add, CliValueKind.ManagedAddress,
        CliValueKind.ManagedAddress)]
    [InlineData(CilOperation.Subtract, CliValueKind.I4, CliValueKind.ManagedAddress)]
    [InlineData(CilOperation.Subtract, CliValueKind.ManagedAddress, CliValueKind.I8)]
    public void ValidatorRejectsEveryInvalidManagedAddressArithmeticShape(
        CilOperation operation,
        CliValueKind left,
        CliValueKind right)
    {
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                2,
                [],
                I(0, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(1, CilOperation.LoadLocal, new CilOperand.Index(1)),
                I(2, operation),
                I(3, CilOperation.Pop),
                I(4, CilOperation.Return)) with
            {
                Locals = [left, right],
            }),
            "operation requires a numeric operand");
    }

    [Theory]
    [InlineData(CliValueKind.I4)]
    [InlineData(CliValueKind.I8)]
    [InlineData(CliValueKind.NativeInt)]
    [InlineData(CliValueKind.ManagedReference)]
    [InlineData(CliValueKind.ManagedAddress)]
    public void ValidatorAcceptsCliBranchConditionTypes(CliValueKind conditionType)
    {
        var body = Body(
            CliValueKind.Void,
            2,
            [],
            I(0, CilOperation.LoadLocal, new CilOperand.Index(0)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Return),
            I(3, CilOperation.Return)) with
        {
            Locals = [conditionType]
        };

        _ = Validate(body);
    }

    [Theory]
    [InlineData(CilOperation.Negate, CliValueKind.I4)]
    [InlineData(CilOperation.Negate, CliValueKind.F4)]
    [InlineData(CilOperation.OnesComplement, CliValueKind.I8)]
    [InlineData(CilOperation.ConvertInt32Unsigned, CliValueKind.I8)]
    [InlineData(CilOperation.ConvertInt64Unsigned, CliValueKind.I4)]
    public void ValidatorAcceptsEveryUnaryNumericOperation(
        CilOperation operation,
        CliValueKind input)
    {
        var load = input switch
        {
            CliValueKind.I4 => I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            CliValueKind.I8 => I(0, CilOperation.LoadInt64, new CilOperand.ConstantI8(1)),
            CliValueKind.F4 => I(0, CilOperation.LoadFloat32, new CilOperand.ConstantF4(1)),
            _ => throw new InvalidOperationException()
        };
        var body = Body(
            CliValueKind.Void,
            1,
            [],
            load,
            I(1, operation),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return));

        _ = Validate(body);
    }

    [Fact]
    public void ValidatorRejectsInvalidIntegerUnaryShiftAndConversionShapes()
    {
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadFloat32, new CilOperand.ConstantF4(1)),
                I(1, CilOperation.OnesComplement),
                I(2, CilOperation.Pop),
                I(3, CilOperation.Return))),
            "operation requires an integer operand");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                2,
                [],
                I(0, CilOperation.LoadInt64, new CilOperand.ConstantI8(1)),
                I(1, CilOperation.LoadInt64, new CilOperand.ConstantI8(2)),
                I(2, CilOperation.ShiftLeft),
                I(3, CilOperation.Pop),
                I(4, CilOperation.Return))),
            "shift count requires an int32 or native integer");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.ConvertNumeric),
                I(2, CilOperation.Pop),
                I(3, CilOperation.Return))),
            "numeric conversion metadata is missing");
    }

    [Fact]
    public void ValidatorAcceptsUnsafeMemoryKernelOperations()
    {
        var body = Body(
            CliValueKind.Void,
            3,
            [CliValueKind.I4, CliValueKind.I4],
            I(0, CilOperation.SizeOf),
            I(1, CilOperation.LocalAllocate),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(4, CilOperation.LoadLocalAddress, new CilOperand.Index(1)),
            I(5, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)),
            I(6, CilOperation.CopyBlock),
            I(7, CilOperation.LoadLocalAddress, new CilOperand.Index(0)),
            I(8, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(9, CilOperation.LoadInt32, new CilOperand.ConstantI4(4)),
            I(10, CilOperation.InitializeBlock),
            I(11, CilOperation.Return));

        _ = Validate(body);
    }

    [Fact]
    public void ValidatorRejectsInvalidLocalAllocationSize()
    {
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadInt64, new CilOperand.ConstantI8(4)),
                I(1, CilOperation.LocalAllocate),
                I(2, CilOperation.Pop),
                I(3, CilOperation.Return))),
            "localloc requires an int32 or native-integer size");
    }

    [Fact]
    public void ValidatorRejectsInvalidUnsafeAddressAndBlockSizeOperands()
    {
        var i4 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                2,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
                I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(2, CilOperation.StoreObject, new CilOperand.TypeIdentity(i4)),
                I(3, CilOperation.Return))),
            "operation requires an address operand");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                3,
                [],
                I(0, CilOperation.LoadNull),
                I(1, CilOperation.LoadNull),
                I(2, CilOperation.LoadInt64, new CilOperand.ConstantI8(4)),
                I(3, CilOperation.CopyBlock),
                I(4, CilOperation.Return))),
            "block size must be int32 or native integer");
    }

    [Theory]
    [InlineData(CilOperation.BranchIfTrue, CliValueKind.I4)]
    [InlineData(CilOperation.BranchIfFalse, CliValueKind.ManagedReference)]
    public void ValidatorAcceptsIntegerAndReferenceConditions(
        CilOperation operation,
        CliValueKind condition)
    {
        var load = condition == CliValueKind.I4
            ? CilOperation.LoadInt32
            : CilOperation.LoadString;
        CilOperand operand = condition == CliValueKind.I4
            ? new CilOperand.ConstantI4(1)
            : new CilOperand.UserString("x");
        var body = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, load, operand),
            I(1, operation, new CilOperand.BranchTarget(3)),
            I(2, CilOperation.Return),
            I(3, CilOperation.Return));

        _ = Validate(body);
    }

    [Fact]
    public void ValidatorRejectsStackAndOperandContractViolations()
    {
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                0,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.Return))),
            "evaluation stack exceeds maxstack");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.I4,
                1,
                [],
                I(0, CilOperation.Return))),
            "evaluation stack underflow");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.Return))),
            "evaluation stack is not empty at return");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadLocal, new CilOperand.Index(2)),
                I(1, CilOperation.Return))),
            "invalid local index 2");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadArgument, new CilOperand.Index(3)),
                I(1, CilOperation.Return))),
            "invalid argument index 3");
    }

    [Fact]
    public void ValidatorRejectsEveryInvalidCallConstructionAndTypeShape()
    {
        var program = new FakeProgram();
        var compatibility = new StackTypeCompatibilityValidator();
        Assert.Throws<ArgumentNullException>(() => new TypedStackValidator(
            null!, program, program, compatibility));
        Assert.Throws<ArgumentNullException>(() =>
            new TypedStackValidator(program, null!, program, compatibility));
        Assert.Throws<ArgumentNullException>(() =>
            new TypedStackValidator(program, program, null!, compatibility));
        Assert.Throws<ArgumentNullException>(() =>
            new TypedStackValidator(program, program, program, null!));
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [CliValueKind.I4],
                I(0, CilOperation.LoadString, new CilOperand.UserString("x")),
                I(1, CilOperation.StoreLocal, new CilOperand.Index(0)),
                I(2, CilOperation.Return))),
            "evaluation stack type mismatch");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [CliValueKind.Void],
                I(0, CilOperation.LoadLocal, new CilOperand.Index(0)),
                I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(3)),
                I(2, CilOperation.Return),
                I(3, CilOperation.Return))),
            "branch condition is not an integer or reference");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.CallVirtual, new CilOperand.Entity(StaticCallKey)),
                I(2, CilOperation.Return))),
            "callvirt targets a static method");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.NewObject, new CilOperand.Entity(StaticCallKey)),
                I(2, CilOperation.Return))),
            "newobj does not target an instance constructor");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.NewObject, new CilOperand.Entity(InstanceCallKey)),
                I(2, CilOperation.Return))),
            "newobj does not target an instance constructor");

        var unknown = Body(
            CliValueKind.Void,
            0,
            [],
            I(0, (CilOperation)int.MaxValue),
            I(1, CilOperation.Return));
        Assert.Throws<InvalidOperationException>(() =>
            CreateValidator(program).Validate(ControlFlowGraphBuilder.Build(unknown)));
    }

    [Fact]
    public void ValidatorRejectsMergesWithDifferentStackShapes()
    {
        var body = Body(
            CliValueKind.Void,
            2,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(4)),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(3, CilOperation.Branch, new CilOperand.BranchTarget(4)),
            I(4, CilOperation.Return));

        AssertDiagnostic(() => Validate(body), "incompatible evaluation stack at merge");
    }

    [Fact]
    public void ExceptionEntriesAreValidatedAndRetainedAsStructuredRegions()
    {
        var finallyBody = Body(
            CliValueKind.Void,
            0,
            [],
            I(0, CilOperation.Nop),
            I(1, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            I(2, CilOperation.Nop),
            I(3, CilOperation.EndFinally),
            I(4, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Finally,
                    0,
                    2,
                    2,
                    2,
                    null,
                    null),
            ],
        };

        var structured = Structurize(finallyBody);

        var group = Assert.Single(structured.ExceptionGroups.Values);
        var clause = Assert.Single(group.Clauses);
        Assert.Equal(CilExceptionRegionKind.Finally, clause.Kind);
        Assert.Equal(
            0,
            structured.Blocks[
                Assert.IsType<Final.StructuredCode>(
                    Assert.IsType<Final.StructuredExceptionCode>(group.ProtectedParts[0])
                        .Body.Regions[0]).Occurrence.Block].StartOffset);
        Assert.Equal(2, structured.Blocks[clause.HandlerBlock].StartOffset);
        Assert.Null(clause.FilterBody);
        Assert.Empty(structured.Blocks[clause.HandlerBlock].EntryStack);
    }

    [Fact]
    public void ValidatorAcceptsManagedThrowStackOperationsAndCatchEntry()
    {
        var body = Body(
            CliValueKind.Void,
            2,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Duplicate),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Throw),
            I(4, CilOperation.Pop),
            I(5, CilOperation.Leave, new CilOperand.BranchTarget(6)),
            I(6, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    0,
                    4,
                    4,
                    2,
                    TypeKey,
                    null),
            ],
        };

        var validated = Validate(body);

        Assert.True(validated.InstructionEntryStacks[4].AsSpan().SequenceEqual(
            [CliValueKind.ManagedReference]));

        var cast = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.CastClass, new CilOperand.Entity(TypeKey)),
            I(2, CilOperation.Pop),
            I(3, CilOperation.Return));
        _ = Validate(cast);
    }

    [Fact]
    public void ValidatorAllowsThrowToAbandonValuesBelowException()
    {
        var body = Body(
            CliValueKind.Void,
            2,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)),
            I(1, CilOperation.LoadNull),
            I(2, CilOperation.Throw));

        _ = Validate(body);
    }

    [Fact]
    public void ValidatorAcceptsUnsignedNonNullReferenceComparison()
    {
        var body = Body(
            CliValueKind.I4,
            2,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.LoadNull),
            I(2, CilOperation.CompareGreaterThanUnsigned),
            I(3, CilOperation.Return));

        _ = Validate(body);
    }

    [Fact]
    public void ValidatorAcceptsAddressArrayDelegateAndTypeOperations()
    {
        var i4 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var field = new FieldInstanceModel(
            new FakeProgram().GetField(StaticFieldKey),
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            i4);
        var body = Body(
            CliValueKind.Void,
            3,
            [],
            I(0, CilOperation.LoadStaticFieldAddress,
                new CilOperand.FieldInstance(field)),
            I(1, CilOperation.Duplicate),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(7)),
            I(3, CilOperation.StoreObject, new CilOperand.TypeIdentity(i4)),
            I(4, CilOperation.LoadObject, new CilOperand.TypeIdentity(i4)),
            I(5, CilOperation.Pop),
            I(6, CilOperation.LoadStaticFieldAddress,
                new CilOperand.Entity(StaticFieldKey)),
            I(7, CilOperation.LoadStaticFieldAddress,
                new CilOperand.Entity(StaticFieldKey)),
            I(8, CilOperation.CopyObject, new CilOperand.TypeIdentity(i4)),
            I(9, CilOperation.LoadStaticFieldAddress,
                new CilOperand.Entity(StaticFieldKey)),
            I(10, CilOperation.InitializeObject, new CilOperand.TypeIdentity(i4)),
            I(11, CilOperation.DefaultValue, new CilOperand.TypeIdentity(i4)),
            I(12, CilOperation.Pop),
            I(13, CilOperation.LoadNull),
            I(14, CilOperation.Unbox, new CilOperand.TypeIdentity(i4)),
            I(15, CilOperation.LoadObject, new CilOperand.TypeIdentity(i4)),
            I(16, CilOperation.Pop),
            I(17, CilOperation.LoadNull),
            I(18, CilOperation.UnboxAny, new CilOperand.TypeIdentity(i4)),
            I(19, CilOperation.Pop),
            I(20, CilOperation.LoadNull),
            I(21, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(22, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(23, CilOperation.StoreArrayElement, new CilOperand.TypeIdentity(i4)),
            I(24, CilOperation.LoadNull),
            I(25, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(26, CilOperation.LoadArrayElementAddress,
                new CilOperand.TypeIdentity(i4)),
            I(27, CilOperation.Pop),
            I(28, CilOperation.LoadNull),
            I(29, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(30, CilOperation.LoadArrayElement, new CilOperand.TypeIdentity(i4)),
            I(31, CilOperation.Pop),
            I(32, CilOperation.LoadNull),
            I(33, CilOperation.LoadNull),
            I(34, CilOperation.DelegateCombine),
            I(35, CilOperation.Pop),
            I(36, CilOperation.LoadNull),
            I(37, CilOperation.LoadNull),
            I(38, CilOperation.DelegateRemove),
            I(39, CilOperation.Pop),
            I(40, CilOperation.LoadNull),
            I(41, CilOperation.LoadNull),
            I(42, CilOperation.DelegateEqual),
            I(43, CilOperation.Pop),
            I(44, CilOperation.LoadNull),
            I(45, CilOperation.LoadNull),
            I(46, CilOperation.DelegateNotEqual),
            I(47, CilOperation.Pop),
            I(48, CilOperation.LoadStaticFieldAddress,
                new CilOperand.Entity(StaticFieldKey)),
            I(49, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(50, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(51, CilOperation.CompareExchange, new CilOperand.TypeIdentity(i4)),
            I(52, CilOperation.Pop),
            I(53, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(54, CilOperation.MaterializeType),
            I(55, CilOperation.GetObjectType),
            I(56, CilOperation.Pop),
            I(57, CilOperation.LoadFunction, new CilOperand.Entity(StaticCallKey)),
            I(58, CilOperation.Pop),
            I(59, CilOperation.LoadNull),
            I(60, CilOperation.LoadVirtualFunction,
                new CilOperand.Entity(InstanceCallKey)),
            I(61, CilOperation.Pop),
            I(62, CilOperation.Return));

        _ = Validate(body);
    }

    [Fact]
    public void ValidatorAcceptsConstrainedValueReceiverAndRejectsMalformedOperands()
    {
        var value = CliTypeIdentity.Named(
            Assembly, "Test", "Value", isValueType: true);
        var constrained = Body(
            CliValueKind.I4,
            2,
            [],
            I(0, CilOperation.LoadStaticFieldAddress,
                new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(2, CilOperation.Constrained, new CilOperand.TypeIdentity(value)),
            I(3, CilOperation.CallVirtual, new CilOperand.Entity(InstanceCallKey)),
            I(4, CilOperation.Return));
        _ = Validate(constrained);

        _ = Validate(Body(
            CliValueKind.ManagedReference,
            1,
            [],
            I(0, CilOperation.LoadStaticFieldAddress,
                new CilOperand.Entity(StaticFieldKey)),
            I(1, CilOperation.Constrained, new CilOperand.TypeIdentity(value)),
            I(2, CilOperation.GetObjectType, new CilOperand.TypeIdentity(value)),
            I(3, CilOperation.Return)));

        var staticConstrained = Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(1, CilOperation.Constrained, new CilOperand.TypeIdentity(value)),
            I(2, CilOperation.Call, new CilOperand.Entity(StaticCallKey)),
            I(3, CilOperation.Return));
        _ = Validate(staticConstrained);

        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.Call, new CilOperand.Entity(NoArgumentStaticCallKey)),
            I(1, CilOperation.Return)));
        _ = Validate(Body(
            CliValueKind.I4,
            1,
            [],
            I(0, CilOperation.Nop),
            I(1, CilOperation.Call, new CilOperand.Entity(NoArgumentStaticCallKey)),
            I(2, CilOperation.Return)));

        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.Constrained, new CilOperand.TypeIdentity(value)),
                I(1, CilOperation.Return))),
            "must immediately precede call or callvirt");
        Assert.Throws<InvalidOperationException>(() => Validate(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadStaticField, new CilOperand.None()),
            I(1, CilOperation.Pop),
            I(2, CilOperation.Return))));
        Assert.Throws<InvalidOperationException>(() => Validate(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.DefaultValue, new CilOperand.None()),
            I(1, CilOperation.Pop),
            I(2, CilOperation.Return))));
    }

    [Fact]
    public void ValidatorSeedsAndChecksExceptionFilters()
    {
        var filter = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.Throw),
            I(2, CilOperation.Pop),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(4, CilOperation.EndFilter),
            I(5, CilOperation.Pop),
            I(6, CilOperation.Leave, new CilOperand.BranchTarget(7)),
            I(7, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Filter,
                    0,
                    2,
                    5,
                    2,
                    null,
                    2),
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    0,
                    2,
                    5,
                    2,
                    TypeKey,
                    null),
            ],
        };

        var validated = Validate(filter);
        Assert.True(validated.InstructionEntryStacks[2].AsSpan().SequenceEqual(
            [CliValueKind.ManagedReference]));

        var incompatibleSeed = filter with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    0,
                    2,
                    0,
                    2,
                    TypeKey,
                    null),
            ],
        };
        AssertDiagnostic(() => Validate(incompatibleSeed), "try and handler ranges overlap");

        var incompatibleEntryStack = Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.Pop),
            I(1, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            I(2, CilOperation.Nop),
            I(3, CilOperation.Leave, new CilOperand.BranchTarget(4)),
            I(4, CilOperation.Return)) with
        {
            ExceptionRegions =
            [
                new CilExceptionRegion(
                    CilExceptionRegionKind.Catch,
                    2,
                    2,
                    0,
                    2,
                    TypeKey,
                    null),
            ],
        };
        AssertDiagnostic(
            () => Validate(incompatibleEntryStack),
            "incompatible exception entry stack");
    }

    [Fact]
    public void ValidatorRejectsMissingCallTargetsAndScalarReceivers()
    {
        Assert.Throws<InvalidOperationException>(() => Validate(Body(
            CliValueKind.Void,
            0,
            [],
            I(0, CilOperation.Call),
            I(1, CilOperation.Return))));
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(1, CilOperation.LoadField, new CilOperand.Entity(InstanceFieldKey)),
                I(2, CilOperation.Pop),
                I(3, CilOperation.Return))),
            "expected managed receiver");
    }

    [Fact]
    public void ValidatorRejectsRectangularArrayOperationsWithScalarTypeOperands()
    {
        var scalar = CliTypeIdentity.Primitive("primitive:i4", CliValueKind.I4);

        var exception = Assert.Throws<InvalidOperationException>(() => Validate(Body(
            CliValueKind.Void,
            1,
            [],
            I(0, CilOperation.NewRectangularArray, new CilOperand.TypeIdentity(scalar)),
            I(1, CilOperation.Pop),
            I(2, CilOperation.Return))));

        Assert.Equal(
            "rectangular-array instruction has no array type operand",
            exception.Message);
    }

    [Fact]
    public void ValidatorAcceptsEveryRectangularArrayStackContract()
    {
        var element = CliTypeIdentity.Primitive("primitive:i4", CliValueKind.I4);
        var array = CliTypeIdentity.Array(element, 2);

        _ = Validate(Body(
            CliValueKind.Void,
            2,
            [],
            I(0, CilOperation.LoadInt32, new CilOperand.ConstantI4(2)),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(3)),
            I(2, CilOperation.NewRectangularArray, new CilOperand.TypeIdentity(array)),
            I(3, CilOperation.Pop),
            I(4, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.Void,
            3,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(3, CilOperation.LoadRectangularArrayElement,
                new CilOperand.TypeIdentity(array)),
            I(4, CilOperation.Pop),
            I(5, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.Void,
            4,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(42)),
            I(4, CilOperation.StoreRectangularArrayElement,
                new CilOperand.TypeIdentity(array)),
            I(5, CilOperation.Return)));

        _ = Validate(Body(
            CliValueKind.Void,
            3,
            [],
            I(0, CilOperation.LoadNull),
            I(1, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            I(2, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            I(3, CilOperation.LoadRectangularArrayElementAddress,
                new CilOperand.TypeIdentity(array)),
            I(4, CilOperation.Pop),
            I(5, CilOperation.Return)));
    }

    [Fact]
    public void ValidatorRejectsPeekAndControlTransferStackErrors()
    {
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.Duplicate),
                I(1, CilOperation.Return))),
            "evaluation stack underflow");
        AssertDiagnostic(
            () => Validate(Body(
                CliValueKind.Void,
                1,
                [],
                I(0, CilOperation.LoadNull),
                I(1, CilOperation.Throw),
                I(2, CilOperation.Pop),
                I(3, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
                I(4, CilOperation.Rethrow)) with
            {
                ExceptionRegions =
                [
                    new CilExceptionRegion(
                        CilExceptionRegionKind.Catch,
                        0,
                        2,
                        2,
                        3,
                        TypeKey,
                        null),
                ],
            }),
            "evaluation stack is not empty at control transfer");
    }
}
