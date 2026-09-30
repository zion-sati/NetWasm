using System.Buffers.Binary;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Layout;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

public sealed class MemberDescriptorDataBuilderTests
{
    private static readonly AssemblyIdentity Assembly = new("Descriptors");
    private static readonly EntityKey DeclaringTypeKey = Key(0x02000001);
    private static readonly EntityKey RuntimeMethodTypeKey = Key(0x02000002);
    private static readonly EntityKey RuntimeConstructorTypeKey = Key(0x02000003);
    private static readonly EntityKey RuntimeFieldTypeKey = Key(0x02000004);
    private static readonly EntityKey RuntimePropertyTypeKey = Key(0x02000005);
    private static readonly EntityKey RuntimeMethodDeclaringTypeFieldKey = Key(0x04000002);
    private static readonly EntityKey RuntimeConstructorDeclaringTypeFieldKey = Key(0x04000003);
    private static readonly EntityKey RuntimeFieldDeclaringTypeFieldKey = Key(0x04000004);
    private static readonly EntityKey RuntimeMethodRequiresDeclaringTypeFieldKey =
        Key(0x04000005);
    private static readonly EntityKey RuntimeConstructorRequiresDeclaringTypeFieldKey =
        Key(0x04000006);
    private static readonly EntityKey RuntimeFieldRequiresDeclaringTypeFieldKey =
        Key(0x04000007);
    private static readonly EntityKey RuntimeMethodFlagsFieldKey = Key(0x04000008);
    private static readonly EntityKey RuntimeMethodReturnTypeFieldKey = Key(0x04000009);
    private static readonly EntityKey RuntimeMethodParameterTypesFieldKey = Key(0x0400000a);
    private static readonly EntityKey RuntimeMethodParameterCountFieldKey = Key(0x0400000b);
    private static readonly EntityKey RuntimeMethodNameFieldKey = Key(0x0400000c);
    private static readonly EntityKey RuntimeMethodPropertyFieldKey = Key(0x0400000d);
    private static readonly EntityKey DuplicateRuntimeMethodFlagsFieldKey = Key(0x0400001a);
    private static readonly EntityKey RuntimeConstructorFlagsFieldKey = Key(0x0400000e);
    private static readonly EntityKey RuntimeConstructorParameterTypesFieldKey = Key(0x0400000f);
    private static readonly EntityKey RuntimeConstructorParameterCountFieldKey = Key(0x04000010);
    private static readonly EntityKey RuntimeConstructorNameFieldKey = Key(0x04000011);
    private static readonly EntityKey RuntimeFieldFlagsFieldKey = Key(0x04000012);
    private static readonly EntityKey RuntimeFieldTypeFieldKey = Key(0x04000013);
    private static readonly EntityKey RuntimeFieldNameFieldKey = Key(0x04000014);
    private static readonly EntityKey RuntimePropertyDeclaringTypeFieldKey = Key(0x04000015);
    private static readonly EntityKey RuntimePropertyTypeFieldKey = Key(0x04000016);
    private static readonly EntityKey RuntimePropertyNameFieldKey = Key(0x04000017);
    private static readonly EntityKey RuntimePropertyGetterFieldKey = Key(0x04000018);
    private static readonly EntityKey RuntimePropertySetterFieldKey = Key(0x04000019);

    [Fact]
    public void EmptyPlanEmitsNoData()
    {
        var state = new ManagedStaticDataBuildState();
        var builder = new MemberDescriptorDataBuilder(
            new RejectingTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new RejectingFieldRepository(),
            Program(),
            Layouts(WasmTargetLayout.Wasm32),
            WasmTargetLayout.Wasm32,
            state);

        builder.Build();

        Assert.Equal(ManagedLayoutSnapshot.StaticDataStart, state.Cursor);
        Assert.Empty(state.Segments);
        Assert.Empty(state.MethodDescriptors);
        Assert.Empty(state.FieldDescriptors);
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EmitsCanonicalObjectsInStableIdentityOrder(WasmTarget targetKind)
    {
        var target = WasmTargetLayout.For(targetKind);
        var declaringType = CliTypeIdentity.Named(
            Assembly,
            "Fixtures",
            "Target",
            isValueType: false);
        var constructedType = CliTypeIdentity.GenericInstantiation(
            declaringType,
            [CliTypeIdentity.Primitive("i4", CliValueKind.I4)]);
        var methodDefinition = Method(0x06000002, "Run") with
        {
            IsPublic = true,
            Signature = MethodSignatureModel.Create(
                CliValueKind.I4,
                CliValueKind.I4),
        };
        var constructorDefinition = Method(0x06000001, ".ctor") with
        {
            Signature = MethodSignatureModel.Create(
                CliValueKind.Void,
                CliValueKind.I4),
        };
        var method = Instance(methodDefinition, declaringType);
        var constructedMethod = Instance(Method(0x06000003, "GenericRun"), constructedType);
        var constructor = Instance(constructorDefinition, declaringType);
        var fieldDefinition = new FieldDefinitionModel(
            Key(0x04000001),
            DeclaringTypeKey,
            "Value",
            CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            isStatic: true)
        {
            IsInitOnly = true,
            IsLiteral = true,
        };
        var directField = new FieldInstanceModel(
            fieldDefinition,
            declaringType,
            fieldDefinition.SignatureType);
        var constructedField = new FieldInstanceModel(
            fieldDefinition,
            constructedType,
            fieldDefinition.SignatureType);
        var program = Program() with
        {
            MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(method.CanonicalName, method)
                .Add(constructedMethod.CanonicalName, constructedMethod)
                .Add(constructor.CanonicalName, constructor),
            FieldDescriptors = ImmutableDictionary<string, FieldInstanceModel>.Empty
                .Add(constructedField.CanonicalName, constructedField)
                .Add(directField.CanonicalName, directField),
        };
        var state = new ManagedStaticDataBuildState();
        var layouts = Layouts(target, declaringType, constructedType);

        new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            program,
            layouts,
            target,
            state).Build();

        Assert.Equal(3, state.MethodDescriptors.Count);
        Assert.Equal(2, state.FieldDescriptors.Count);
        Assert.Equal(7, state.Segments.Count);
        Assert.Equal(target.ObjectHeaderSize, state.MemberDescriptorDeclaringTypeIdOffset);
        Assert.Equal(
            target.ObjectHeaderSize + sizeof(int),
            state.MemberDescriptorRequiresDeclaringTypeOffset);
        Assert.All(
            state.MethodDescriptors.Values.Concat(state.FieldDescriptors.Values),
            address => Assert.Equal(0, address % target.ObjectReferenceAlignment));
        Assert.All(state.Segments, segment =>
            Assert.Equal(0, segment.Address % sizeof(int)));
        Assert.Contains(state.Segments, segment =>
            BinaryPrimitives.ReadInt32LittleEndian(segment.Data.AsSpan()) == 101);
        Assert.Contains(state.Segments, segment =>
            BinaryPrimitives.ReadInt32LittleEndian(segment.Data.AsSpan()) == 102);
        Assert.Equal(
            2,
            state.Segments.Count(segment =>
                BinaryPrimitives.ReadInt32LittleEndian(segment.Data.AsSpan()) == 103));
        Assert.Equal(
            layouts.ObjectIdentities[declaringType].TypeId,
            ReadDeclaringTypeId(state, state.MethodDescriptors[method.CanonicalName]));
        Assert.Equal(
            layouts.ObjectIdentities[declaringType].TypeId,
            ReadDeclaringTypeId(state, state.FieldDescriptors[directField.CanonicalName]));
        Assert.Equal(
            layouts.ConstructedObjects[constructedType].TypeId,
            ReadDeclaringTypeId(
                state,
                state.FieldDescriptors[constructedField.CanonicalName]));
        Assert.Equal(0, ReadRequiresDeclaringType(
            state,
            state.MethodDescriptors[method.CanonicalName]));
        Assert.Equal(1, ReadRequiresDeclaringType(
            state,
            state.MethodDescriptors[constructedMethod.CanonicalName]));
        Assert.Equal(0, ReadRequiresDeclaringType(
            state,
            state.FieldDescriptors[directField.CanonicalName]));
        Assert.Equal(1, ReadRequiresDeclaringType(
            state,
            state.FieldDescriptors[constructedField.CanonicalName]));
        Assert.Equal(
            (int)(RuntimeMemberDescriptorFlags.Static |
                  RuntimeMemberDescriptorFlags.Public),
            ReadField(
                state,
                state.MethodDescriptors[method.CanonicalName],
                layouts.ValueLayoutState.Fields[RuntimeMethodFlagsFieldKey].Offset));
        Assert.Equal(
            layouts.ObjectIdentities[CliTypeIdentity.FromStackKind(CliValueKind.I4)].TypeId,
            ReadField(
                state,
                state.MethodDescriptors[method.CanonicalName],
                layouts.ValueLayoutState.Fields[RuntimeMethodReturnTypeFieldKey].Offset));
        Assert.Equal(
            layouts.ObjectIdentities[CliTypeIdentity.FromStackKind(CliValueKind.I4)].TypeId,
            ReadField(
                state,
                ReadAddress(
                    state,
                    state.MethodDescriptors[method.CanonicalName],
                    layouts.ValueLayoutState.Fields[RuntimeMethodParameterTypesFieldKey].Offset,
                    target),
                0));
        Assert.Equal(
            (int)(RuntimeMemberDescriptorFlags.Static |
                  RuntimeMemberDescriptorFlags.InitOnly |
                  RuntimeMemberDescriptorFlags.Literal),
            ReadField(
                state,
                state.FieldDescriptors[directField.CanonicalName],
                layouts.ValueLayoutState.Fields[RuntimeFieldFlagsFieldKey].Offset));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EncodesOpenGenericMethodFlags(WasmTarget targetKind)
    {
        var target = WasmTargetLayout.For(targetKind);
        var openDeclaringType = CliTypeIdentity.GenericInstantiation(
            DeclaringType(),
            [CliTypeIdentity.GenericParameter(method: false, 0)]);
        var definition = Method(0x06000001, "Open") with { GenericArity = 1 };
        var method = new MethodInstanceModel(
            definition,
            openDeclaringType,
            [],
            definition.Signature);
        var state = new ManagedStaticDataBuildState();
        var layouts = Layouts(target, constructedType: openDeclaringType);

        new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            Program() with
            {
                MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty
                    .Add(method.CanonicalName, method),
            },
            layouts,
            target,
            state).Build();

        Assert.Equal(
            (int)(RuntimeMemberDescriptorFlags.Static |
                  RuntimeMemberDescriptorFlags.GenericMethod |
                  RuntimeMemberDescriptorFlags.GenericMethodDefinition |
                  RuntimeMemberDescriptorFlags.ContainsGenericParameters),
            ReadField(
                state,
                state.MethodDescriptors[method.CanonicalName],
                layouts.ValueLayoutState.Fields[RuntimeMethodFlagsFieldKey].Offset));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void MarksOnlyCompilerPlannedMethodsAsExpressionExecutable(
        WasmTarget targetKind)
    {
        var target = WasmTargetLayout.For(targetKind);
        var declaringType = DeclaringType();
        var executable = Instance(Method(0x06000001, "Executable") with
        {
            IsStatic = false,
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
        }, declaringType);
        var descriptorOnly = Instance(Method(0x06000002, "DescriptorOnly") with
        {
            IsStatic = false,
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
        }, declaringType);
        var program = Program() with
        {
            MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(executable.CanonicalName, executable)
                .Add(descriptorOnly.CanonicalName, descriptorOnly),
            MemberExecution = new MemberExecutionPlan(
                ImmutableDictionary<string, MemberMethodExecutionPlan>.Empty.Add(
                    executable.CanonicalName,
                    new(executable, RequiresDispatch: false, [])),
                ImmutableDictionary<string, FieldInstanceModel>.Empty,
                UnsupportedTarget: null),
        };
        var state = new ManagedStaticDataBuildState();
        var layouts = Layouts(target, declaringType);

        new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            program,
            layouts,
            target,
            state).Build();

        var flagsOffset =
            layouts.ValueLayoutState.Fields[RuntimeMethodFlagsFieldKey].Offset;
        Assert.Equal(
            (int)RuntimeMemberDescriptorFlags.ExpressionExecutable,
            ReadField(
                state,
                state.MethodDescriptors[executable.CanonicalName],
                flagsOffset));
        Assert.Equal(
            0,
            ReadField(
                state,
                state.MethodDescriptors[descriptorOnly.CanonicalName],
                flagsOffset));
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void EmitsPropertyCrossReferencesAndOnlyDemandedNames(WasmTarget targetKind)
    {
        var target = WasmTargetLayout.For(targetKind);
        var declaringType = DeclaringType();
        var getter = Instance(Method(0x06000001, "get_Value") with
        {
            IsStatic = false,
            IsPublic = true,
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
        }, declaringType);
        var setter = Instance(Method(0x06000002, "set_Value") with
        {
            IsStatic = false,
            Signature = MethodSignatureModel.Create(
                CliValueKind.Void,
                CliValueKind.I4),
        }, declaringType);
        var property = new PropertyInstanceModel(
            new PropertyDefinitionModel(
                Key(0x17000001),
                DeclaringTypeKey,
                "Value",
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                [],
                getter.Definition.Key,
                setter.Definition.Key),
            declaringType,
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            [],
            getter,
            setter);
        var program = Program() with
        {
            MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty
                .Add(getter.CanonicalName, getter)
                .Add(setter.CanonicalName, setter),
            PropertyDescriptors = ImmutableDictionary<string, PropertyInstanceModel>.Empty
                .Add(property.CanonicalName, property),
            NamedMemberDescriptors = ImmutableHashSet.Create(
                StringComparer.Ordinal,
                getter.CanonicalName,
                property.CanonicalName),
        };
        var state = new ManagedStaticDataBuildState();
        state.Strings.Add("get_Value", new StringLayout(64, 9, 8));
        state.Strings.Add("Value", new StringLayout(96, 5, 8));
        var layouts = Layouts(target, declaringType);

        new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            program,
            layouts,
            target,
            state).Build();

        var getterAddress = state.MethodDescriptors[getter.CanonicalName];
        var setterAddress = state.MethodDescriptors[setter.CanonicalName];
        var propertyAddress = state.PropertyDescriptors[property.CanonicalName];
        Assert.Equal(propertyAddress, ReadAddress(
            state,
            getterAddress,
            layouts.ValueLayoutState.Fields[RuntimeMethodPropertyFieldKey].Offset,
            target));
        Assert.Equal(propertyAddress, ReadAddress(
            state,
            setterAddress,
            layouts.ValueLayoutState.Fields[RuntimeMethodPropertyFieldKey].Offset,
            target));
        Assert.Equal(getterAddress, ReadAddress(
            state,
            propertyAddress,
            layouts.ValueLayoutState.Fields[RuntimePropertyGetterFieldKey].Offset,
            target));
        Assert.Equal(setterAddress, ReadAddress(
            state,
            propertyAddress,
            layouts.ValueLayoutState.Fields[RuntimePropertySetterFieldKey].Offset,
            target));
        Assert.Equal(64, ReadAddress(
            state,
            getterAddress,
            layouts.ValueLayoutState.Fields[RuntimeMethodNameFieldKey].Offset,
            target));
        Assert.Equal(0, ReadAddress(
            state,
            setterAddress,
            layouts.ValueLayoutState.Fields[RuntimeMethodNameFieldKey].Offset,
            target));
        Assert.Equal(96, ReadAddress(
            state,
            propertyAddress,
            layouts.ValueLayoutState.Fields[RuntimePropertyNameFieldKey].Offset,
            target));
        Assert.Equal((int)RuntimeMemberDescriptorFlags.Public, ReadField(
            state,
            getterAddress,
            layouts.ValueLayoutState.Fields[RuntimeMethodFlagsFieldKey].Offset));
        Assert.Equal(0, ReadField(
            state,
            setterAddress,
            layouts.ValueLayoutState.Fields[RuntimeMethodFlagsFieldKey].Offset));
        Assert.Equal(
            layouts.ObjectIdentities[CliTypeIdentity.FromStackKind(CliValueKind.I4)].TypeId,
            ReadField(
                state,
                propertyAddress,
            layouts.ValueLayoutState.Fields[RuntimePropertyTypeFieldKey].Offset));
    }

    [Fact]
    public void EmitsPropertyWithoutAccessors()
    {
        var target = WasmTargetLayout.Wasm32;
        var declaringType = DeclaringType();
        var property = new PropertyInstanceModel(
            new PropertyDefinitionModel(
                Key(0x17000001),
                DeclaringTypeKey,
                "Value",
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                [],
                null,
                null),
            declaringType,
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            [],
            null,
            null);
        var state = new ManagedStaticDataBuildState();
        var layouts = Layouts(target, declaringType);

        new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            Program() with
            {
                PropertyDescriptors =
                    ImmutableDictionary<string, PropertyInstanceModel>.Empty.Add(
                        property.CanonicalName,
                        property),
            },
            layouts,
            target,
            state).Build();

        var address = state.PropertyDescriptors[property.CanonicalName];
        Assert.Equal(0, ReadAddress(
            state,
            address,
            layouts.ValueLayoutState.Fields[RuntimePropertyGetterFieldKey].Offset,
            target));
        Assert.Equal(0, ReadAddress(
            state,
            address,
            layouts.ValueLayoutState.Fields[RuntimePropertySetterFieldKey].Offset,
            target));
    }

    [Fact]
    public void AccessorAssociatedWithMultiplePropertiesFailsWithRuntimeContract()
    {
        var declaringType = DeclaringType();
        var getter = Instance(Method(0x06000001, "get_Value") with
        {
            IsStatic = false,
            Signature = MethodSignatureModel.Create(CliValueKind.I4),
        }, declaringType);
        var first = Property(0x17000001, "First", getter);
        var second = Property(0x17000002, "Second", getter);

        AssertRuntimeContract(() => new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            Program() with
            {
                MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty
                    .Add(getter.CanonicalName, getter),
                PropertyDescriptors = ImmutableDictionary<string, PropertyInstanceModel>.Empty
                    .Add(first.CanonicalName, first)
                    .Add(second.CanonicalName, second),
            },
            Layouts(WasmTargetLayout.Wasm32, declaringType),
            WasmTargetLayout.Wasm32,
            new ManagedStaticDataBuildState()).Build());

        PropertyInstanceModel Property(int token, string name, MethodInstanceModel accessor) =>
            new(
                new PropertyDefinitionModel(
                    Key(token),
                    DeclaringTypeKey,
                    name,
                    CliTypeIdentity.FromStackKind(CliValueKind.I4),
                    [],
                    accessor.Definition.Key,
                    null),
                declaringType,
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                [],
                accessor,
                null);
    }

    [Fact]
    public void MissingDemandedNameStorageFailsWithRuntimeContract()
    {
        var declaringType = DeclaringType();
        var method = Instance(Method(0x06000001, "Run"), declaringType);

        AssertRuntimeContract(() => new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            Program() with
            {
                MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty
                    .Add(method.CanonicalName, method),
                NamedMemberDescriptors = ImmutableHashSet.Create(
                    StringComparer.Ordinal,
                    method.CanonicalName),
            },
            Layouts(WasmTargetLayout.Wasm32, declaringType),
            WasmTargetLayout.Wasm32,
            new ManagedStaticDataBuildState()).Build());
    }

    [Fact]
    public void MissingAccessorDescriptorStorageFailsWithRuntimeContract()
    {
        var declaringType = DeclaringType();
        var getter = Instance(Method(0x06000001, "get_Value"), declaringType);
        var property = new PropertyInstanceModel(
            new PropertyDefinitionModel(
                Key(0x17000001),
                DeclaringTypeKey,
                "Value",
                CliTypeIdentity.FromStackKind(CliValueKind.I4),
                [],
                getter.Definition.Key,
                null),
            declaringType,
            CliTypeIdentity.FromStackKind(CliValueKind.I4),
            [],
            getter,
            null);

        AssertRuntimeContract(() => new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            Program() with
            {
                PropertyDescriptors =
                    ImmutableDictionary<string, PropertyInstanceModel>.Empty.Add(
                        property.CanonicalName,
                        property),
            },
            Layouts(WasmTargetLayout.Wasm32, declaringType),
            WasmTargetLayout.Wasm32,
            new ManagedStaticDataBuildState()).Build());
    }

    [Fact]
    public void BuilderRejectsMissingDependencies()
    {
        var finder = new RejectingTypeFinder();
        var fields = new RejectingFieldRepository();
        var program = Program();
        var target = WasmTargetLayout.Wasm32;
        var layouts = Layouts(target);
        var state = new ManagedStaticDataBuildState();

        Assert.Throws<ArgumentNullException>(() =>
            new MemberDescriptorDataBuilder(
                null!,
                new DescriptorTypeDefinitionResolver(),
                fields,
                program,
                layouts,
                target,
                state));
        Assert.Throws<ArgumentNullException>(() =>
            new MemberDescriptorDataBuilder(
                finder,
                null!,
                fields,
                program,
                layouts,
                target,
                state));
        Assert.Throws<ArgumentNullException>(() =>
            new MemberDescriptorDataBuilder(
                finder,
                new DescriptorTypeDefinitionResolver(),
                null!,
                program,
                layouts,
                target,
                state));
        Assert.Throws<ArgumentNullException>(() =>
            new MemberDescriptorDataBuilder(
                finder,
                new DescriptorTypeDefinitionResolver(),
                fields,
                null!,
                layouts,
                target,
                state));
        Assert.Throws<ArgumentNullException>(() =>
            new MemberDescriptorDataBuilder(
                finder,
                new DescriptorTypeDefinitionResolver(),
                fields,
                program,
                null!,
                target,
                state));
        Assert.Throws<ArgumentNullException>(() =>
            new MemberDescriptorDataBuilder(
                finder,
                new DescriptorTypeDefinitionResolver(),
                fields,
                program,
                layouts,
                null!,
                state));
        Assert.Throws<ArgumentNullException>(() =>
            new MemberDescriptorDataBuilder(
                finder,
                new DescriptorTypeDefinitionResolver(),
                fields,
                program,
                layouts,
                target,
                null!));
    }

    [Fact]
    public void MissingDescriptorObjectLayoutFailsWithRuntimeContract()
    {
        var declaringType = DeclaringType();
        var method = Instance(Method(0x06000001, "Run"), declaringType);
        var layouts = Layouts(WasmTargetLayout.Wasm32, declaringType) with
        {
            Objects = Layouts(WasmTargetLayout.Wasm32, declaringType)
                .Objects.Remove(RuntimeMethodTypeKey),
        };

        AssertRuntimeContract(() => new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            Program() with
            {
                MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty
                    .Add(method.CanonicalName, method),
            },
            layouts,
            WasmTargetLayout.Wasm32,
            new ManagedStaticDataBuildState()).Build());
    }

    [Fact]
    public void MissingDescriptorFieldLayoutFailsWithRuntimeContract()
    {
        var declaringType = DeclaringType();
        var method = Instance(Method(0x06000001, "Run"), declaringType);
        var layouts = Layouts(WasmTargetLayout.Wasm32, declaringType) with
        {
            ValueLayoutState = new ManagedValueLayoutState(),
        };

        AssertRuntimeContract(() => new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            Program() with
            {
                MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty
                    .Add(method.CanonicalName, method),
            },
            layouts,
            WasmTargetLayout.Wasm32,
            new ManagedStaticDataBuildState()).Build());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrDuplicateDescriptorFieldDefinitionFailsWithRuntimeContract(
        bool duplicate)
    {
        var declaringType = DeclaringType();
        var method = Instance(Method(0x06000001, "Run"), declaringType);
        var fields = RuntimeMethodFields().ToList();
        if (duplicate)
        {
            fields.Add(DuplicateRuntimeMethodFlagsFieldKey);
        }
        else
        {
            fields.Remove(RuntimeMethodFlagsFieldKey);
        }

        AssertRuntimeContract(() => new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder([.. fields]),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            Program() with
            {
                MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty
                    .Add(method.CanonicalName, method),
            },
            Layouts(WasmTargetLayout.Wasm32, declaringType),
            WasmTargetLayout.Wasm32,
            new ManagedStaticDataBuildState()).Build());
    }

    [Fact]
    public void DescriptorTypesMustShareTheDeclaringTypeFieldOffset()
    {
        var declaringType = DeclaringType();
        var method = Instance(Method(0x06000002, "Run"), declaringType);
        var constructor = Instance(Method(0x06000001, ".ctor"), declaringType);
        var layouts = Layouts(WasmTargetLayout.Wasm32, declaringType);
        layouts.ValueLayoutState.Fields[RuntimeMethodDeclaringTypeFieldKey] =
            new FieldLayout(WasmTargetLayout.Wasm32.ObjectHeaderSize + sizeof(int));

        AssertRuntimeContract(() => new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            Program() with
            {
                MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty
                    .Add(method.CanonicalName, method)
                    .Add(constructor.CanonicalName, constructor),
            },
            layouts,
            WasmTargetLayout.Wasm32,
            new ManagedStaticDataBuildState()).Build());
    }

    [Fact]
    public void MissingDeclaringTypeLayoutFailsWithRuntimeContract()
    {
        var declaringType = DeclaringType();
        var method = Instance(Method(0x06000001, "Run"), declaringType);

        AssertRuntimeContract(() => new MemberDescriptorDataBuilder(
            new DescriptorTypeFinder(),
            new DescriptorTypeDefinitionResolver(),
            new DescriptorFieldRepository(),
            Program() with
            {
                MethodDescriptors = ImmutableDictionary<string, MethodInstanceModel>.Empty
                    .Add(method.CanonicalName, method),
            },
            Layouts(WasmTargetLayout.Wasm32),
            WasmTargetLayout.Wasm32,
            new ManagedStaticDataBuildState()).Build());
    }

    [Fact]
    public void StaticDataProviderPublishesDescriptorAddressesAndRejectsMissingOnes()
    {
        var declaringType = DeclaringType();
        var method = Instance(Method(0x06000001, "Run"), declaringType);
        var fieldDefinition = new FieldDefinitionModel(
            Key(0x04000001),
            DeclaringTypeKey,
            "Value",
            CliValueKind.I4,
            IsStatic: false);
        var field = new FieldInstanceModel(
            fieldDefinition,
            declaringType,
            fieldDefinition.SignatureType);
        var data = new ManagedStaticData(256, [], [], [], [], [], [], [], [], [])
        {
            MethodDescriptors = ImmutableDictionary<string, int>.Empty.Add(
                method.CanonicalName,
                64),
            FieldDescriptors = ImmutableDictionary<string, int>.Empty.Add(
                field.CanonicalName,
                80),
            MemberDescriptorDeclaringTypeIdOffset = 4,
            MemberDescriptorRequiresDeclaringTypeOffset = 8,
        };
        var provider = new StaticDataLayoutProvider(
            new ManagedLayoutSnapshot(Layouts(WasmTargetLayout.Wasm32), data));

        Assert.Equal(64, provider.GetMethodDescriptorAddress(method));
        Assert.Equal(80, provider.GetFieldDescriptorAddress(field));
        Assert.Equal(4, provider.DeclaringTypeIdOffset);
        Assert.Equal(8, provider.RequiresDeclaringTypeOffset);

        var missingMethod = Instance(Method(0x06000002, "Missing"), declaringType);
        var missingFieldDefinition = fieldDefinition with { Key = Key(0x04000002) };
        var missingField = new FieldInstanceModel(
            missingFieldDefinition,
            declaringType,
            missingFieldDefinition.SignatureType);
        AssertRuntimeContract(() => provider.GetMethodDescriptorAddress(missingMethod));
        AssertRuntimeContract(() => provider.GetFieldDescriptorAddress(missingField));
    }

    private static ReachableProgram Program()
    {
        var entry = Method(0x06000010, "Entry");
        return new(
            entry,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            []);
    }

    private static ManagedTypeLayouts Layouts(
        WasmTargetLayout target,
        CliTypeIdentity? declaringType = null,
        CliTypeIdentity? constructedType = null)
    {
        var values = new ManagedValueLayoutState();
        var i4 = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var native = CliTypeIdentity.FromStackKind(CliValueKind.NativeInt);
        var reference = CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference);
        var methodLayout = DescriptorLayout(
            101,
            (RuntimeMethodDeclaringTypeFieldKey, i4),
            (RuntimeMethodRequiresDeclaringTypeFieldKey, i4),
            (RuntimeMethodFlagsFieldKey, i4),
            (RuntimeMethodReturnTypeFieldKey, i4),
            (RuntimeMethodParameterTypesFieldKey, native),
            (RuntimeMethodParameterCountFieldKey, i4),
            (RuntimeMethodNameFieldKey, reference),
            (RuntimeMethodPropertyFieldKey, reference));
        var constructorLayout = DescriptorLayout(
            102,
            (RuntimeConstructorDeclaringTypeFieldKey, i4),
            (RuntimeConstructorRequiresDeclaringTypeFieldKey, i4),
            (RuntimeConstructorFlagsFieldKey, i4),
            (RuntimeConstructorParameterTypesFieldKey, native),
            (RuntimeConstructorParameterCountFieldKey, i4),
            (RuntimeConstructorNameFieldKey, reference));
        var fieldLayout = DescriptorLayout(
            103,
            (RuntimeFieldDeclaringTypeFieldKey, i4),
            (RuntimeFieldRequiresDeclaringTypeFieldKey, i4),
            (RuntimeFieldFlagsFieldKey, i4),
            (RuntimeFieldTypeFieldKey, i4),
            (RuntimeFieldNameFieldKey, reference));
        var propertyLayout = DescriptorLayout(
            104,
            (RuntimePropertyDeclaringTypeFieldKey, i4),
            (RuntimePropertyTypeFieldKey, i4),
            (RuntimePropertyNameFieldKey, reference),
            (RuntimePropertyGetterFieldKey, reference),
            (RuntimePropertySetterFieldKey, reference));
        var identityBuilder = ImmutableDictionary.CreateBuilder<
            CliTypeIdentity,
            ObjectLayout>();
        identityBuilder.Add(i4, new ObjectLayout(301, target.ObjectHeaderSize, []));
        identityBuilder.Add(
            CliTypeIdentity.FromStackKind(CliValueKind.Void),
            new ObjectLayout(302, target.ObjectHeaderSize, []));
        if (declaringType is not null)
        {
            identityBuilder.Add(
                declaringType,
                new ObjectLayout(201, target.ObjectHeaderSize, []));
        }
        var constructed = constructedType is null
            ? ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty
            : ImmutableDictionary<CliTypeIdentity, ObjectLayout>.Empty.Add(
                constructedType,
                new ObjectLayout(202, target.ObjectHeaderSize, []));
        return new ManagedTypeLayouts(
            ImmutableDictionary<EntityKey, ObjectLayout>.Empty
                .Add(RuntimeMethodTypeKey, methodLayout)
                .Add(RuntimeConstructorTypeKey, constructorLayout)
                .Add(RuntimeFieldTypeKey, fieldLayout)
                .Add(RuntimePropertyTypeKey, propertyLayout),
            identityBuilder.ToImmutable(),
            [],
            constructed,
            values,
            target,
            0,
            0,
            0,
            0);

        ObjectLayout DescriptorLayout(
            int typeId,
            params (EntityKey Field, CliTypeIdentity Type)[] fields)
        {
            var cursor = target.ObjectHeaderSize;
            var references = ImmutableArray.CreateBuilder<int>();
            foreach (var (field, typeIdentity) in fields)
            {
                var alignment = target.GetStorageAlignment(typeIdentity);
                cursor = WasmTargetLayout.Align(cursor, alignment);
                var size = target.GetStorageSize(typeIdentity);
                values.Fields.Add(field, new FieldLayout(cursor)
                {
                    Size = size,
                    Type = typeIdentity,
                });
                if (typeIdentity.StackKind == CliValueKind.ManagedReference)
                {
                    references.Add(cursor);
                }
                cursor += size;
            }
            return new ObjectLayout(
                typeId,
                WasmTargetLayout.Align(cursor, target.ObjectReferenceAlignment),
                references.ToImmutable());
        }
    }

    private static int ReadDeclaringTypeId(
        ManagedStaticDataBuildState state,
        int address)
    {
        var segment = state.Segments.Single(candidate => candidate.Address == address);
        return BinaryPrimitives.ReadInt32LittleEndian(
            segment.Data.AsSpan().Slice(
                state.MemberDescriptorDeclaringTypeIdOffset!.Value));
    }

    private static int ReadRequiresDeclaringType(
        ManagedStaticDataBuildState state,
        int address)
    {
        var segment = state.Segments.Single(candidate => candidate.Address == address);
        return BinaryPrimitives.ReadInt32LittleEndian(
            segment.Data.AsSpan().Slice(
                state.MemberDescriptorRequiresDeclaringTypeOffset!.Value));
    }

    private static int ReadField(
        ManagedStaticDataBuildState state,
        int address,
        int offset)
    {
        var segment = state.Segments.Single(candidate => candidate.Address == address);
        return BinaryPrimitives.ReadInt32LittleEndian(
            segment.Data.AsSpan().Slice(offset));
    }

    private static int ReadAddress(
        ManagedStaticDataBuildState state,
        int address,
        int offset,
        WasmTargetLayout target)
    {
        var segment = state.Segments.Single(candidate => candidate.Address == address);
        return target.AddressSize == sizeof(int)
            ? BinaryPrimitives.ReadInt32LittleEndian(
                segment.Data.AsSpan().Slice(offset))
            : checked((int)BinaryPrimitives.ReadInt64LittleEndian(
                segment.Data.AsSpan().Slice(offset)));
    }

    private static MethodDefinitionModel Method(int token, string name) => new(
        Key(token),
        DeclaringTypeKey,
        name,
        IsStatic: true,
        MethodSignatureModel.Create(CliValueKind.Void),
        1);

    private static MethodInstanceModel Instance(
        MethodDefinitionModel definition,
        CliTypeIdentity declaringType) => new(
            definition,
            declaringType,
            [],
            definition.Signature);

    private static CliTypeIdentity DeclaringType() => CliTypeIdentity.Named(
        Assembly,
        "Fixtures",
        "Target",
        isValueType: false);

    private static void AssertRuntimeContract(Action action)
    {
        var exception = Assert.Throws<CompilerException>(action);
        Assert.Equal(DiagnosticCode.RuntimeContract, exception.Diagnostic.Code);
    }

    private static EntityKey Key(int token) => new(Assembly, token);

    private static ImmutableArray<EntityKey> RuntimeMethodFields() =>
    [
        RuntimeMethodDeclaringTypeFieldKey,
        RuntimeMethodRequiresDeclaringTypeFieldKey,
        RuntimeMethodFlagsFieldKey,
        RuntimeMethodReturnTypeFieldKey,
        RuntimeMethodParameterTypesFieldKey,
        RuntimeMethodParameterCountFieldKey,
        RuntimeMethodNameFieldKey,
        RuntimeMethodPropertyFieldKey,
    ];

    private sealed class DescriptorTypeFinder(
        ImmutableArray<EntityKey> runtimeMethodFields = default) : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) => fullName switch
        {
            "System.Reflection.RuntimeMethodInfo" => Type(
                RuntimeMethodTypeKey,
                "RuntimeMethodInfo"),
            "System.Reflection.RuntimeConstructorInfo" => Type(
                RuntimeConstructorTypeKey,
                "RuntimeConstructorInfo"),
            "System.Reflection.RuntimeFieldInfo" => Type(
                RuntimeFieldTypeKey,
                "RuntimeFieldInfo"),
            "System.Reflection.RuntimePropertyInfo" => Type(
                RuntimePropertyTypeKey,
                "RuntimePropertyInfo"),
            _ => throw new InvalidOperationException(fullName),
        };

        private TypeDefinitionModel Type(EntityKey key, string name) => new(
            key,
            "System.Reflection",
            name,
            IsValueType: false,
            key == RuntimeMethodTypeKey
                ? runtimeMethodFields.IsDefault
                    ? RuntimeMethodFields()
                    : runtimeMethodFields
                : key == RuntimeConstructorTypeKey
                    ? [RuntimeConstructorDeclaringTypeFieldKey,
                        RuntimeConstructorRequiresDeclaringTypeFieldKey,
                        RuntimeConstructorFlagsFieldKey,
                        RuntimeConstructorParameterTypesFieldKey,
                        RuntimeConstructorParameterCountFieldKey,
                        RuntimeConstructorNameFieldKey]
                    : key == RuntimeFieldTypeKey
                        ? [RuntimeFieldDeclaringTypeFieldKey,
                            RuntimeFieldRequiresDeclaringTypeFieldKey,
                            RuntimeFieldFlagsFieldKey,
                            RuntimeFieldTypeFieldKey,
                            RuntimeFieldNameFieldKey]
                        : [RuntimePropertyDeclaringTypeFieldKey,
                            RuntimePropertyTypeFieldKey,
                            RuntimePropertyNameFieldKey,
                            RuntimePropertyGetterFieldKey,
                            RuntimePropertySetterFieldKey],
            []);
    }

    private sealed class DescriptorTypeDefinitionResolver : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => new(
            DeclaringTypeKey,
            "Fixtures",
            "Target",
            IsValueType: false,
            [],
            [])
        {
            GenericArity = identity.Shape == CliTypeShape.GenericInstantiation ? 1 : 0,
        };
    }

    private sealed class RejectingTypeFinder : ITypeFinder
    {
        public TypeDefinitionModel FindType(string fullName) =>
            throw new InvalidOperationException(fullName);
    }

    private sealed class DescriptorFieldRepository : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key)
        {
            var (name, type) = key switch
            {
                _ when key == RuntimeMethodDeclaringTypeFieldKey ||
                    key == RuntimeConstructorDeclaringTypeFieldKey ||
                    key == RuntimeFieldDeclaringTypeFieldKey =>
                    ("DeclaringTypeId", CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                _ when key == RuntimeMethodRequiresDeclaringTypeFieldKey ||
                    key == RuntimeConstructorRequiresDeclaringTypeFieldKey ||
                    key == RuntimeFieldRequiresDeclaringTypeFieldKey =>
                    ("RequiresDeclaringType", CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                _ when key == RuntimeMethodFlagsFieldKey ||
                    key == DuplicateRuntimeMethodFlagsFieldKey ||
                    key == RuntimeConstructorFlagsFieldKey ||
                    key == RuntimeFieldFlagsFieldKey =>
                    ("Flags", CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                _ when key == RuntimeMethodReturnTypeFieldKey =>
                    ("ReturnTypeId", CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                _ when key == RuntimeMethodParameterTypesFieldKey ||
                    key == RuntimeConstructorParameterTypesFieldKey =>
                    ("ParameterTypeIds", CliTypeIdentity.FromStackKind(CliValueKind.NativeInt)),
                _ when key == RuntimeMethodParameterCountFieldKey ||
                    key == RuntimeConstructorParameterCountFieldKey =>
                    ("ParameterCount", CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                _ when key == RuntimeMethodNameFieldKey ||
                    key == RuntimeConstructorNameFieldKey ||
                    key == RuntimeFieldNameFieldKey =>
                    ("MetadataName", CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)),
                _ when key == RuntimeMethodPropertyFieldKey =>
                    ("Property", CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)),
                _ when key == RuntimeFieldTypeFieldKey =>
                    ("FieldTypeId", CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                _ when key == RuntimePropertyDeclaringTypeFieldKey =>
                    ("DeclaringTypeId", CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                _ when key == RuntimePropertyTypeFieldKey =>
                    ("PropertyTypeId", CliTypeIdentity.FromStackKind(CliValueKind.I4)),
                _ when key == RuntimePropertyNameFieldKey =>
                    ("MetadataName", CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)),
                _ when key == RuntimePropertyGetterFieldKey =>
                    ("Getter", CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)),
                _ when key == RuntimePropertySetterFieldKey =>
                    ("Setter", CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference)),
                _ => throw new InvalidOperationException(key.ToString()),
            };
            return new FieldDefinitionModel(
                key,
                DeclaringType(key),
                name,
                type,
                isStatic: false);
        }

        private static EntityKey DeclaringType(EntityKey field) =>
            field == RuntimeMethodDeclaringTypeFieldKey ||
            field == RuntimeMethodRequiresDeclaringTypeFieldKey ||
            field == RuntimeMethodFlagsFieldKey ||
            field == DuplicateRuntimeMethodFlagsFieldKey ||
            field == RuntimeMethodReturnTypeFieldKey ||
            field == RuntimeMethodParameterTypesFieldKey ||
            field == RuntimeMethodParameterCountFieldKey ||
            field == RuntimeMethodNameFieldKey ||
            field == RuntimeMethodPropertyFieldKey
                ? RuntimeMethodTypeKey
                : field == RuntimeConstructorDeclaringTypeFieldKey ||
                  field == RuntimeConstructorRequiresDeclaringTypeFieldKey ||
                  field == RuntimeConstructorFlagsFieldKey ||
                  field == RuntimeConstructorParameterTypesFieldKey ||
                  field == RuntimeConstructorParameterCountFieldKey ||
                  field == RuntimeConstructorNameFieldKey
                    ? RuntimeConstructorTypeKey
                    : field == RuntimeFieldDeclaringTypeFieldKey ||
                      field == RuntimeFieldRequiresDeclaringTypeFieldKey ||
                      field == RuntimeFieldFlagsFieldKey ||
                      field == RuntimeFieldTypeFieldKey ||
                      field == RuntimeFieldNameFieldKey
                        ? RuntimeFieldTypeKey
                        : RuntimePropertyTypeKey;
    }

    private sealed class RejectingFieldRepository : IFieldRepository
    {
        public FieldDefinitionModel GetField(EntityKey key) =>
            throw new InvalidOperationException(key.ToString());
    }
}
