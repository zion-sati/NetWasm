using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.Tests;

public sealed class CoreModelTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, 12, 16, 20, 24)]
    [InlineData(WasmTarget.Wasm64, 24, 28, 32, 40)]
    public void RectangularArrayLayoutIsTargetParametric(
        WasmTarget target,
        int elementTypeIdOffset,
        int expectedRankOffset,
        int expectedShapeOffset,
        int expectedSize)
    {
        var layout = RectangularArrayLayout.Create(
            target == WasmTarget.Wasm32
                ? WasmTargetLayout.Wasm32
                : WasmTargetLayout.Wasm64,
            elementTypeIdOffset);

        Assert.Equal(expectedRankOffset, layout.RankOffset);
        Assert.Equal(expectedShapeOffset, layout.ShapePointerOffset);
        Assert.Equal(expectedSize, layout.ObjectSize);
        Assert.Equal(8, layout.DimensionSize);
        Assert.Equal(0, layout.DimensionLengthOffset);
        Assert.Equal(4, layout.DimensionStrideOffset);
    }

    private static readonly AssemblyIdentity Assembly = new("Fixture");
    private static readonly EntityKey TypeKey = new(Assembly, 0x02000001);

    [Fact]
    public void TargetLayoutsKeepSemanticIdsFixedWhileWideningAddresses()
    {
        var wasm32 = WasmTargetLayout.For(WasmTarget.Wasm32);
        var wasm64 = WasmTargetLayout.For(WasmTarget.Wasm64);
        var reference = CliTypeIdentity.Named(
            Assembly, "Example", "Node", isValueType: false);
        var address = CliTypeIdentity.ManagedByReference(reference);

        Assert.False(wasm32.UsesMemory64);
        Assert.True(wasm64.UsesMemory64);
        Assert.Equal(4, wasm32.AddressSize);
        Assert.Equal(8, wasm64.AddressSize);
        Assert.Equal(4, wasm32.ObjectReferenceSize);
        Assert.Equal(8, wasm64.ObjectReferenceSize);
        Assert.Equal(4, wasm32.ObjectHeaderSize);
        Assert.Equal(8, wasm64.ObjectHeaderSize);
        Assert.Equal(4, WasmTargetLayout.TypeIdSize);
        Assert.Equal(4, WasmTargetLayout.SemanticLengthSize);
        Assert.Equal(4, wasm32.GetStorageSize(reference));
        Assert.Equal(8, wasm64.GetStorageSize(reference));
        Assert.Equal(4, wasm32.GetStorageSize(address));
        Assert.Equal(8, wasm64.GetStorageSize(address));
        var scalarKinds = ImmutableArray.Create(
            CliValueKind.I4,
            CliValueKind.I8,
            CliValueKind.F4,
            CliValueKind.F8,
            CliValueKind.NativeInt);
        foreach (var kind in scalarKinds)
        {
            var type = CliTypeIdentity.FromStackKind(kind);
            var expected = kind switch
            {
                CliValueKind.I4 or CliValueKind.F4 => 4,
                CliValueKind.I8 or CliValueKind.F8 => 8,
                CliValueKind.NativeInt => wasm64.AddressSize,
                _ => throw new InvalidOperationException(),
            };
            Assert.Equal(expected, wasm64.GetStorageSize(type));
            Assert.Equal(expected, wasm64.GetStorageAlignment(type));
        }
        Assert.Equal(4, wasm32.GetStorageAlignment(reference));
        Assert.Equal(8, wasm64.GetStorageAlignment(reference));
        Assert.Equal(4, wasm32.GetStorageAlignment(address));
        Assert.Equal(8, wasm64.GetStorageAlignment(address));
        var voidType = CliTypeIdentity.FromStackKind(CliValueKind.Void);
        Assert.Throws<ArgumentOutOfRangeException>(() => wasm32.GetStorageSize(voidType));
        Assert.Throws<ArgumentOutOfRangeException>(() => wasm32.GetStorageAlignment(voidType));
        Assert.Equal(16, WasmTargetLayout.Align(12, 8));
        Assert.Throws<OverflowException>(() => WasmTargetLayout.Align(int.MaxValue, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => WasmTargetLayout.For((WasmTarget)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => WasmTargetLayout.Align(-1, 8));
        Assert.Throws<ArgumentOutOfRangeException>(() => WasmTargetLayout.Align(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => WasmTargetLayout.Align(1, 3));
    }

    [Fact]
    public void HostInteropModelsPreserveWideConstantsAndManifestLayout()
    {
        var integer = new CilOperand.ConstantI8(long.MaxValue);
        var single = new CilOperand.ConstantF4(1.25f);
        var @double = new CilOperand.ConstantF8(2.5d);
        var manifest = new HostInteropManifest(
            1,
            "wasm64",
            new HostInteropStatusAbi(0, 1, 0),
            new HostInteropTargetLayout(8, 8, 12, 8, 16),
            [],
            []);

        Assert.Equal(long.MaxValue, integer.Value);
        Assert.Equal(1.25f, single.Value);
        Assert.Equal(2.5d, @double.Value);
        Assert.Equal(1, manifest.Version);
        Assert.Equal(8, manifest.TargetLayout.ArrayLengthOffset);
    }

    [Fact]
    public void ComponentAndAsyncInteropContractsPreserveTheirSemanticShape()
    {
        var task = CliTypeIdentity.Named(
            Assembly, "System.Threading.Tasks", "Task", isValueType: false);
        var valueTask = CliTypeIdentity.Named(
            Assembly, "System.Threading.Tasks", "ValueTask", isValueType: true);
        var resultType = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var genericTask = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                Assembly, "System.Threading.Tasks", "Task`1", isValueType: false),
            [resultType]);
        var genericValueTask = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                Assembly, "System.Threading.Tasks", "ValueTask`1", isValueType: true),
            [resultType]);
        var ordinary = CliTypeIdentity.Named(
            Assembly, "Example", "Result", isValueType: false);
        var genericOrdinary = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                Assembly, "Example", "Result`1", isValueType: false),
            [resultType]);

        var taskReturn = JavaScriptAsyncSignature.Classify(task);
        var valueTaskReturn = JavaScriptAsyncSignature.Classify(valueTask);
        var genericTaskReturn = JavaScriptAsyncSignature.Classify(genericTask);
        var genericValueTaskReturn = JavaScriptAsyncSignature.Classify(genericValueTask);
        Assert.Equal(JavaScriptAsyncReturnKind.Task, taskReturn.Kind);
        Assert.True(taskReturn.IsAsync);
        Assert.False(taskReturn.HasResult);
        Assert.Equal(JavaScriptAsyncReturnKind.ValueTask, valueTaskReturn.Kind);
        Assert.Equal(resultType, genericTaskReturn.ResultType);
        Assert.Equal(resultType, genericValueTaskReturn.ResultType);
        Assert.False(JavaScriptAsyncSignature.Classify(ordinary).IsAsync);
        Assert.False(JavaScriptAsyncSignature.Classify(genericOrdinary).IsAsync);

        var type = new CanonicalAbiType(CanonicalAbiTypeKind.U32, resultType);
        var field = new CanonicalAbiField("value", type);
        var @case = new CanonicalAbiCase("some", type);
        var parameter = new CanonicalAbiParameter("input", type);
        var function = new CanonicalAbiFunction(
            "example:contract@1.0.0/api",
            "run",
            new EntityKey(Assembly, 0x06000001),
            [parameter],
            type);
        var contract = new ComponentBoundaryContract(
            "example:contract@1.0.0",
            "application",
            [function],
            [function]);
        Assert.Equal("value", field.Name);
        Assert.Same(type, field.Type);
        Assert.Equal("some", @case.Name);
        Assert.Same(type, @case.Type);
        Assert.Equal("input", parameter.Name);
        Assert.Same(type, parameter.Type);
        Assert.Equal("example:contract@1.0.0", contract.Package);
        Assert.Equal("application", contract.World);
        Assert.Same(function, contract.Imports[0]);
        Assert.Same(function, contract.Exports[0]);
        Assert.False(contract.IsEmpty);
        Assert.True(ComponentBoundaryContract.Empty.IsEmpty);
    }

    [Fact]
    public void DiagnosticsFormatEveryLocationShape()
    {
        var withoutLocation = new CompilerDiagnostic(
            DiagnosticCode.InvalidCommandLine,
            "bad option");
        var withMethod = new CompilerDiagnostic(
            DiagnosticCode.InvalidCil,
            "bad body",
            "Example::Run");
        var withOffset = new CompilerDiagnostic(
            DiagnosticCode.UnsupportedCil,
            "bad opcode",
            "Example::Run",
            10);

        Assert.Equal("NW1000", withoutLocation.Id);
        Assert.Equal("NW1000: bad option", withoutLocation.ToString());
        Assert.Equal("NW1002: Example::Run: bad body", withMethod.ToString());
        Assert.Equal(
            "NW1001: Example::Run IL_000a: bad opcode",
            withOffset.ToString());

        var exception = new CompilerException(withOffset);
        Assert.Same(withOffset, exception.Diagnostic);
        Assert.Equal(withOffset.ToString(), exception.Message);
        Assert.Equal(
            "NWA2001",
            new CompilerDiagnostic(DiagnosticCode.GenericExpansion, "limit").Id);
    }

    [Fact]
    public void IdentitiesAndEntityKeysHaveStableText()
    {
        Assert.Equal("Fixture", Assembly.ToString());
        Assert.Equal("Fixture:0x02000001", TypeKey.ToString());
    }

    [Fact]
    public void StackKindReplacementPreservesTypeIdentity()
    {
        var element = CliTypeIdentity.Named(
            Assembly,
            "Example",
            "Element",
            isValueType: true);
        var identity = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(
                Assembly,
                "Example",
                "Container`1",
                isValueType: true),
            [element]);

        var replaced = identity.WithStackKind(CliValueKind.I4);

        Assert.Equal(identity.CanonicalName, replaced.CanonicalName);
        Assert.Equal(identity.Shape, replaced.Shape);
        Assert.Equal(CliValueKind.I4, replaced.StackKind);
        Assert.Equal(identity.IsValueType, replaced.IsValueType);
        Assert.Equal(identity.ElementType, replaced.ElementType);
        Assert.Equal(identity.TypeArguments, replaced.TypeArguments);
        Assert.Equal(identity.GenericParameterIndex, replaced.GenericParameterIndex);
        Assert.Equal(identity.ArrayRank, replaced.ArrayRank);
        Assert.Equal(identity.Assembly, replaced.Assembly);
        Assert.Equal(identity.FullName, replaced.FullName);
    }

    [Fact]
    public void RootMapsPreserveDirectIndirectAndConstructorSafepoints()
    {
        var direct = new RootSource(RootSourceKind.Argument, 1);
        var indirect = new RootSource(
            RootSourceKind.ManagedAddressLocal,
            2,
            12);
        var safepoint = new SafepointRootMap(7, [direct], [indirect]);
        var map = new MethodRootMap(
            new EntityKey(Assembly, 0x06000002),
            ImmutableDictionary<RootSource, int>.Empty
                .Add(direct, 0)
                .Add(indirect, 1),
            ImmutableDictionary<int, SafepointRootMap>.Empty.Add(7, safepoint));

        Assert.False(direct.IsIndirect);
        Assert.True(indirect.IsIndirect);
        Assert.Equal(RootSourceKind.Argument, direct.Kind);
        Assert.Equal(1, direct.Index);
        Assert.Equal(2, map.SlotCount);
        Assert.Equal(new EntityKey(Assembly, 0x06000002), map.Method);
        Assert.Equal(7, map.Safepoints[7].IlOffset);
        Assert.Equal(direct, map.Safepoints[7].Roots.Single());
        Assert.Equal(indirect, map.Safepoints[7].ConstructorCallRoots.Single());
        Assert.Equal(
            [RootSourceKind.Argument, RootSourceKind.Local,
                RootSourceKind.EvaluationStack,
                RootSourceKind.ManagedAddressArgument,
                RootSourceKind.ManagedAddressLocal,
                RootSourceKind.ManagedAddressEvaluationStack,
                RootSourceKind.AllocationTemporary],
            Enum.GetValues<RootSourceKind>());
    }

    [Fact]
    public void LayoutModelsPreserveValueFieldAndDescriptorMetadata()
    {
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Example",
            "Value",
            isValueType: true);
        var value = new ValueLayout(valueType, 8, 4, []);
        var field = new FieldLayout(12)
        {
            Size = 8,
            Type = valueType,
        };
        var finalizer = new EntityKey(Assembly, 0x06000003);
        var descriptor = new TypeDescriptorLayout(
            TypeKey, 10, 9, 24, 100, 3, finalizer);
        var constructed = new ConstructedTypeDescriptorLayout(
            valueType, 11, 10, 32, 104, 4, "Example.Value::Finalize");
        var valueDescriptor = new ValueTypeDescriptorLayout(
            valueType, 12, 8, 108, 2);

        Assert.Equal(valueType, value.Type);
        Assert.Equal(4, value.Alignment);
        Assert.Equal(valueType, field.Type);
        Assert.Equal(finalizer, descriptor.Finalizer);
        Assert.Equal(11, constructed.TypeId);
        Assert.Equal(10, constructed.BaseTypeId);
        Assert.Equal(32, constructed.ObjectSize);
        Assert.Equal(104, constructed.BitmapAddress);
        Assert.Equal(4, constructed.BitmapBitCount);
        Assert.Equal("Example.Value::Finalize", constructed.Finalizer);
        Assert.Equal(valueType, valueDescriptor.Type);
        Assert.Equal(12, valueDescriptor.TypeId);
        Assert.Equal(8, valueDescriptor.Size);
        Assert.Equal(108, valueDescriptor.BitmapAddress);
        Assert.Equal(2, valueDescriptor.BitmapBitCount);
    }

    [Fact]
    public void CilOperandBodyAndExceptionModelsPreserveDecodedMetadata()
    {
        var type = CliTypeIdentity.Named(
            Assembly, "Example", "Node", isValueType: false);
        var fieldDefinition = new FieldDefinitionModel(
            new EntityKey(Assembly, 0x04000002),
            TypeKey,
            "Next",
            type,
            false);
        var field = new FieldInstanceModel(fieldDefinition, type, type);
        var signature = MethodSignatureModel.Create(type, type);
        var localTypes = ImmutableArray.Create(type);
        var method = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000004),
            TypeKey,
            "Run",
            true,
            signature,
            1);
        var body = new CilMethodBody(method, 1, [CliValueKind.ManagedReference], [])
        {
            LocalSignatureTypes = localTypes,
        };
        var region = new CilExceptionRegion(
            CilExceptionRegionKind.Catch,
            0,
            4,
            4,
            2,
            TypeKey,
            null);

        Assert.True(new CilOperand.SwitchTargets([1, 3, 5]).Offsets
            .SequenceEqual([1, 3, 5]));
        Assert.Equal(field, new CilOperand.FieldInstance(field).Value);
        Assert.Equal(type, new CilOperand.TypeIdentity(type).Value);
        Assert.Equal(signature, new CilOperand.CallSite(signature).Signature);
        var conversion = new CilOperand.NumericConversion(64, true, true, false, true);
        Assert.Equal(64, conversion.BitWidth);
        Assert.True(conversion.DestinationUnsigned);
        Assert.False(conversion.SourceUnsigned);
        Assert.True(new CilOperand.ByteData([1, 2, 3]).Value
            .SequenceEqual([(byte)1, (byte)2, (byte)3]));
        Assert.Equal(localTypes, body.LocalSignatureTypes);
        Assert.Equal(CilExceptionRegionKind.Catch, region.Kind);
        Assert.Equal(4, region.HandlerOffset);
        Assert.Equal(2, region.HandlerLength);
        Assert.Equal(TypeKey, region.CatchType);
    }

    [Fact]
    public void ExtendedTypeMethodAndDispatchModelsPreserveSemanticMetadata()
    {
        var reference = CliTypeIdentity.Named(
            Assembly, "Example", "Node", isValueType: false);
        var pointer = CliTypeIdentity.UnmanagedPointer(reference);
        var type = new TypeDefinitionModel(
            TypeKey, "Example", "Node", false, [], [])
        {
            LayoutKind = CliTypeLayoutKind.Explicit,
            PackingSize = 4,
            DeclaredSize = 24,
            InlineArrayLength = 3,
            GenericArity = 1,
            IsInterface = true,
            IsAbstract = true,
            IsSealed = true,
            IsEnum = true,
        };
        var export = new InteropExportDeclaration("run");
        var witImport = new WitImportDeclaration("api", "read");
        var witExport = new WitExportDeclaration("api", "write");
        var postReturn = new WitPostReturnDeclaration("api", "post-write");
        var definition = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000005),
            TypeKey,
            "Run",
            false,
            MethodSignatureModel.Create(CliValueKind.Void),
            1)
        {
            GenericArity = 2,
            IsVirtual = true,
            IsNewSlot = true,
            IsFinal = true,
            IsAbstract = true,
            JSExport = export,
            WitImport = witImport,
            WitExport = witExport,
            WitPostReturn = postReturn,
        };
        var instance = new MethodInstanceModel(
            definition, reference, [], definition.Signature);
        var target = new DispatchTargetModel(reference, instance);
        var callSite = new DispatchCallSiteModel(
            "Caller", 16, instance, [target]);
        var typeTest = new TypeTestSiteModel(
            "Caller", 32, reference, [reference]);

        Assert.Equal(Assembly, reference.Assembly);
        Assert.Equal(reference, pointer.ElementType);
        Assert.Equal(CliTypeLayoutKind.Explicit, type.LayoutKind);
        Assert.Equal(4, type.PackingSize);
        Assert.Equal(24, type.DeclaredSize);
        Assert.Equal(3, type.InlineArrayLength);
        Assert.Equal(1, type.GenericArity);
        Assert.True(type.IsInterface);
        Assert.True(type.IsAbstract);
        Assert.True(type.IsSealed);
        Assert.True(type.IsEnum);
        Assert.Equal(2, definition.GenericArity);
        Assert.True(definition.IsVirtual);
        Assert.True(definition.IsNewSlot);
        Assert.True(definition.IsFinal);
        Assert.True(definition.IsAbstract);
        Assert.Same(export, definition.JSExport);
        Assert.Same(witImport, definition.WitImport);
        Assert.Same(witExport, definition.WitExport);
        Assert.Same(postReturn, definition.WitPostReturn);
        Assert.Equal(reference, target.ReceiverType);
        Assert.Same(instance, target.Method);
        Assert.Equal("Caller", callSite.Caller);
        Assert.Equal(16, callSite.IlOffset);
        Assert.Same(instance, callSite.Declaration);
        Assert.Equal(target, callSite.Targets.Single());
        Assert.Equal("Caller@00000010", callSite.Key);
        Assert.Equal("Caller", typeTest.Caller);
        Assert.Equal(32, typeTest.IlOffset);
        Assert.Equal(reference, typeTest.TargetType);
        Assert.Equal(reference, typeTest.MatchingTypes.Single());
        Assert.Equal("Caller@00000020", typeTest.Key);
    }

    [Theory]
    [InlineData("", "Widget", "Widget")]
    [InlineData("Example", "Widget", "Example.Widget")]
    public void TypeFullNameHandlesGlobalAndNamedNamespaces(
        string typeNamespace,
        string name,
        string expected)
    {
        var type = new TypeDefinitionModel(
            TypeKey,
            typeNamespace,
            name,
            false,
            [],
            []);

        Assert.Equal(expected, type.FullName);
        Assert.Equal(TypeKey, type.Key);
        Assert.Equal(typeNamespace, type.Namespace);
        Assert.Equal(name, type.Name);
        Assert.False(type.IsValueType);
        Assert.Empty(type.Fields);
        Assert.Empty(type.Methods);
    }

    [Fact]
    public void FieldAndMethodModelsExposeTheirCliAndWasmShapes()
    {
        var field = new FieldDefinitionModel(
            new EntityKey(Assembly, 0x04000001),
            TypeKey,
            "Value",
            CliValueKind.I4,
            false);
        var signature = MethodSignatureModel.Create(
            CliValueKind.I4,
            CliValueKind.I4);
        var instance = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000001),
            TypeKey,
            "Read",
            false,
            signature,
            1);
        var staticWithoutBody = instance with
        {
            IsStatic = true,
            RelativeVirtualAddress = 0,
        };

        Assert.Equal("Example.Widget::Value", field.DisplayName("Example.Widget"));
        Assert.Equal(new EntityKey(Assembly, 0x04000001), field.Key);
        Assert.Equal(TypeKey, field.DeclaringType);
        Assert.Equal("Value", field.Name);
        Assert.Equal(CliValueKind.I4, field.FieldType);
        Assert.False(field.IsStatic);
        Assert.Equal(CliValueKind.I4, signature.ReturnType);
        Assert.True(signature.ParameterTypes.SequenceEqual([CliValueKind.I4]));
        Assert.Equal(new EntityKey(Assembly, 0x06000001), instance.Key);
        Assert.Equal(TypeKey, instance.DeclaringType);
        Assert.Equal("Read", instance.Name);
        Assert.False(instance.IsStatic);
        Assert.Same(signature, instance.Signature);
        Assert.Equal(1, instance.RelativeVirtualAddress);
        Assert.True(instance.HasBody);
        Assert.False(staticWithoutBody.HasBody);
        Assert.True(instance.WasmParameterTypes.SequenceEqual(
            [CliValueKind.ManagedReference, CliValueKind.I4]));
        Assert.True(staticWithoutBody.WasmParameterTypes.SequenceEqual(
            [CliValueKind.I4]));
    }

    [Fact]
    public void StructuralSignatureIdentityIsIndependentFromWasmStackKind()
    {
        var assembly = new AssemblyIdentity("Example");
        var text = CliTypeIdentity.Named(
            assembly, "Example", "Text", isValueType: false);
        var node = CliTypeIdentity.Named(
            assembly, "Example", "Node", isValueType: false);
        var textAgain = CliTypeIdentity.Named(
            assembly, "Example", "Text", isValueType: false);
        var generic = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.Named(assembly, "Example", "Box`1", isValueType: false),
            [text]);

        Assert.Equal(CliValueKind.ManagedReference, text.StackKind);
        Assert.Equal(CliValueKind.ManagedReference, node.StackKind);
        Assert.Equal(text, textAgain);
        Assert.NotEqual(text, node);
        Assert.Equal("[Example]Example.Box`1<[Example]Example.Text>", generic.CanonicalName);
        Assert.True(generic.TypeArguments.AsSpan().SequenceEqual([text]));
        Assert.Equal(CliTypeShape.GenericInstantiation, generic.Shape);
    }

    [Fact]
    public void VectorAndRankOneArrayIdentitiesRemainDistinctThroughComposition()
    {
        var element = CliTypeIdentity.Named(
            Assembly, "Example", "Node", isValueType: false);
        var vector = CliTypeIdentity.SzArray(element);
        var rankOne = CliTypeIdentity.Array(element, 1);
        var wrapper = CliTypeIdentity.Named(
            Assembly, "Example", "Wrapper`1", isValueType: false);
        var vectorWrapper = CliTypeIdentity.GenericInstantiation(wrapper, [vector]);
        var rankOneWrapper = CliTypeIdentity.GenericInstantiation(wrapper, [rankOne]);
        var identities = new HashSet<CliTypeIdentity>
        {
            vector,
            rankOne,
            vectorWrapper,
            rankOneWrapper,
        };

        Assert.Equal($"{element.CanonicalName}[]", vector.CanonicalName);
        Assert.Equal($"{element.CanonicalName}[*]", rankOne.CanonicalName);
        Assert.NotEqual(vector, rankOne);
        Assert.NotEqual(vectorWrapper, rankOneWrapper);
        Assert.Equal(4, identities.Count);
        Assert.Equal(
            CliTypeIdentity.Array(element, 1),
            CliTypeIdentity.Array(
                CliTypeIdentity.GenericParameter(method: false, index: 0),
                1).Substitute([element]));
    }

    [Fact]
    public void StructuralTypeIdentitiesCoverEverySupportedShapeAndSubstitution()
    {
        var scalar = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
        var reference = CliTypeIdentity.Named(
            Assembly, "Example", "Node", isValueType: false);
        var value = CliTypeIdentity.Named(
            Assembly, "Example", "Pair", isValueType: true);
        var typeParameter = CliTypeIdentity.GenericParameter(false, 0);
        var methodParameter = CliTypeIdentity.GenericParameter(true, 0);
        Assert.True(typeParameter.ContainsGenericParameters);
        Assert.True(methodParameter.ContainsGenericParameters);
        var vector = CliTypeIdentity.SzArray(typeParameter);
        var matrix = CliTypeIdentity.Array(methodParameter, 2);
        var address = CliTypeIdentity.ManagedByReference(typeParameter);
        var generic = CliTypeIdentity.GenericInstantiation(reference,
            [typeParameter, methodParameter]);

        Assert.True(vector.ContainsGenericParameters);
        Assert.True(matrix.ContainsGenericParameters);
        Assert.True(address.ContainsGenericParameters);
        Assert.True(generic.ContainsGenericParameters);
        Assert.False(reference.ContainsGenericParameters);
        Assert.Equal("[Fixture]Example.Node", reference.ToString());
        Assert.Equal(2, matrix.ArrayRank);
        Assert.Equal(reference, typeParameter.Substitute([reference]));
        Assert.Equal(value, methodParameter.Substitute([], [value]));
        Assert.Equal(value, methodParameter.Substitute(default, [value]));
        Assert.Same(typeParameter, typeParameter.Substitute([]));
        Assert.Same(methodParameter, methodParameter.Substitute([], []));
        Assert.Equal(reference, vector.Substitute([reference]).ElementType);
        Assert.Equal(value, matrix.Substitute([], [value]).ElementType);
        Assert.Equal(reference, address.Substitute([reference]).ElementType);
        Assert.True(generic.Substitute([reference], [value]).TypeArguments
            .AsSpan().SequenceEqual([reference, value]));
        Assert.Same(scalar, scalar.Substitute([reference], [value]));
        Assert.True(reference.Equals((object)CliTypeIdentity.Named(
            Assembly, "Example", "Node", isValueType: false)));
        Assert.False(reference.Equals(new object()));
        Assert.False(reference.Equals((CliTypeIdentity?)null));
        Assert.Equal(reference.GetHashCode(), CliTypeIdentity.Named(
            Assembly, "Example", "Node", isValueType: false).GetHashCode());

        Assert.Equal(CliValueKind.Void,
            CliTypeIdentity.FromStackKind(CliValueKind.Void).StackKind);
        Assert.Equal(CliValueKind.I4,
            CliTypeIdentity.FromStackKind(CliValueKind.I4).StackKind);
        Assert.Equal(CliValueKind.ManagedReference,
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference).StackKind);
        Assert.Equal(CliValueKind.ValueType,
            CliTypeIdentity.FromStackKind(CliValueKind.ValueType).StackKind);
        Assert.Equal(CliValueKind.ManagedAddress,
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedAddress).StackKind);
        Assert.Equal(CliValueKind.Unknown,
            CliTypeIdentity.FromStackKind(CliValueKind.Unknown).StackKind);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CliTypeIdentity.FromStackKind((CliValueKind)int.MaxValue));

        Assert.Equal(CliGenericContext.Empty, default(CliGenericContext).Normalize());
        var context = new CliGenericContext([reference], [value]);
        Assert.Equal(context, context.Normalize());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CliTypeIdentity.Array(reference, 0));
    }

    [Fact]
    public void ConstructedModelsAndDefaultLayoutContractsRemainCoherent()
    {
        var declaringType = CliTypeIdentity.Named(
            Assembly, "Example", "Box`1", isValueType: false);
        var value = CliTypeIdentity.Named(
            Assembly, "Example", "Pair", isValueType: true);
        var constructed = CliTypeIdentity.GenericInstantiation(
            declaringType, [value]);
        var method = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000003),
            TypeKey,
            "Map",
            true,
            MethodSignatureModel.Create(value, value),
            1);
        var instance = new MethodInstanceModel(
            method,
            constructed,
            [value],
            method.Signature);
        var field = new FieldDefinitionModel(
            new EntityKey(Assembly, 0x04000002),
            TypeKey,
            "Item",
            value,
            false);
        var fieldInstance = new FieldInstanceModel(field, constructed, value);
        var implementation = new MethodImplementationModel(method, method);
        var implementationInstance = new MethodImplementationInstanceModel(
            instance, instance);

        Assert.True(instance.IsConstructed);
        Assert.Contains("<", instance.CanonicalName);
        Assert.True((instance with
        {
            DeclaringType = CliTypeIdentity.Array(value, 2),
            MethodArguments = [],
        }).IsConstructed);
        Assert.False((instance with
        {
            DeclaringType = declaringType,
            MethodArguments = [],
        }).IsConstructed);
        Assert.True(fieldInstance.IsConstructed);
        Assert.Contains("0x04000002", fieldInstance.CanonicalName);
        Assert.Same(method, implementation.Body);
        Assert.Same(method, implementation.Declaration);
        Assert.Same(instance, implementationInstance.Body);
        Assert.Same(instance, implementationInstance.Declaration);
        var substituted = method.Signature.Substitute([value], [value]);
        Assert.Equal(value, substituted.ReturnSignatureType);
        Assert.True(substituted.ParameterSignatureTypes.AsSpan().SequenceEqual([value]));

        var layout = new MinimalLayoutProvider();
        Assert.Equal(sizeof(int), layout.GetValueLayout(value).Size);
        Assert.False(layout.GetValueLayout(value).ContainsReferences);
        Assert.True(layout.GetValueLayout(
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference))
            .ContainsReferences);
        Assert.False(layout.GetValueLayout(
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference))
            .IsFlattenedAbiEligible);
        Assert.True(new ValueLayout(value, 8, 4, []).IsFlattenedAbiEligible);
        Assert.False(new ValueLayout(value, 0, 4, []).IsFlattenedAbiEligible);
        Assert.False(new ValueLayout(value, 12, 4, []).IsFlattenedAbiEligible);
        Assert.Equal(7, layout.GetFieldLayout(fieldInstance).Offset);
        Assert.Equal(11, layout.GetStaticFieldLayout(fieldInstance).Address);
        Assert.Equal(1, layout.GetObjectLayout(constructed).TypeId);
        Assert.Equal(3, layout.TypeTypeId);
        Assert.Equal(-1, layout.DelegateTargetOffset);
        Assert.Equal(-1, layout.DelegateMethodIdOffset);
        Assert.Equal(-1, layout.DelegateLeftOffset);
        Assert.Equal(-1, layout.DelegateRightOffset);
        Assert.Empty(layout.ConstructedTypeDescriptors);
        Assert.Empty(layout.ValueTypeDescriptors);

        var descriptor = new ConstructedTypeDescriptorLayout(
            constructed, 3, 2, 12, 32, 2, null);
        Assert.Equal(constructed, descriptor.Type);
    }

    [Fact]
    public void SupportedCilRegistryContainsEveryDeclaredOperationOnce()
    {
        Assert.Equal(Enum.GetValues<CilOperation>(), SupportedCil.Operations);
        Assert.Equal(
            SupportedCil.Operations.Length,
            SupportedCil.Operations.Distinct().Count());
    }

    [Fact]
    public void CilRecordsPreserveEveryOperandAndBodyProperty()
    {
        var none = new CilOperand.None();
        var constant = new CilOperand.ConstantI4(-2);
        var index = new CilOperand.Index(3);
        var target = new CilOperand.BranchTarget(9);
        var entity = new CilOperand.Entity(TypeKey);
        var text = new CilOperand.UserString("value");
        var instruction = new CilInstruction(
            1,
            2,
            CilOperation.LoadInt32,
            constant);
        var method = new MethodDefinitionModel(
            new EntityKey(Assembly, 0x06000002),
            TypeKey,
            "Run",
            true,
            MethodSignatureModel.Create(CliValueKind.Void),
            1);
        var body = new CilMethodBody(
            method,
            1,
            [CliValueKind.I4],
            [instruction]);

        Assert.NotNull(none);
        Assert.Equal(-2, constant.Value);
        Assert.Equal(3, index.Value);
        Assert.Equal(9, target.Offset);
        Assert.Equal(TypeKey, entity.Key);
        Assert.Equal("value", text.Value);
        Assert.Equal(1, instruction.Offset);
        Assert.Equal(2, instruction.NextOffset);
        Assert.Equal(CilOperation.LoadInt32, instruction.Operation);
        Assert.Same(constant, instruction.Operand);
        Assert.Same(method, body.Method);
        Assert.Equal(1, body.MaxStack);
        Assert.Equal(CliValueKind.I4, Assert.Single(body.Locals));
        Assert.Same(instruction, Assert.Single(body.Instructions));
    }

    [Fact]
    public void LayoutAndAbiContractsRetainTheirValues()
    {
        var objectLayout = new ObjectLayout(2, 12, [8]);
        var data = new DataSegment(8, [1, 2, 3]);
        Assert.Equal(2, objectLayout.TypeId);
        Assert.Equal(12, objectLayout.Size);
        Assert.Equal(8, Assert.Single(objectLayout.ReferenceOffsets));
        var descriptor = new TypeDescriptorLayout(TypeKey, 2, 1, 12, 32, 3, null);
        Assert.Equal(TypeKey, descriptor.Type);
        Assert.Equal(2, descriptor.TypeId);
        Assert.Equal(1, descriptor.BaseTypeId);
        Assert.Equal(12, descriptor.ObjectSize);
        Assert.Equal(32, descriptor.BitmapAddress);
        Assert.Equal(3, descriptor.BitmapBitCount);
        Assert.Equal(8, new FieldLayout(8).Offset);
        Assert.Equal(16, new StaticFieldLayout(16).Address);
        var stringLayout = new StringLayout(20, 4, 8);
        Assert.Equal(20, stringLayout.Address);
        Assert.Equal(4, stringLayout.Length);
        Assert.Equal(8, stringLayout.DataOffset);
        Assert.Equal(8, data.Address);
        Assert.Equal(3, data.Data.Length);
        Assert.Equal("netwasm.runtime.v1", RuntimeAbi.RuntimeModule);
        Assert.Equal("netwasm.host.v1", RuntimeAbi.HostModule);
        Assert.Equal("initialize", RuntimeAbi.RuntimeInitialize);
        Assert.Equal("register_type", RuntimeAbi.RuntimeRegisterType);
        Assert.Equal("register_static_root", RuntimeAbi.RuntimeRegisterStaticRoot);
        Assert.Equal("root_frame_enter", RuntimeAbi.RuntimeRootFrameEnter);
        Assert.Equal("root_frame_leave", RuntimeAbi.RuntimeRootFrameLeave);
        Assert.Equal("value_frame_enter", RuntimeAbi.RuntimeValueFrameEnter);
        Assert.Equal("value_frame_leave", RuntimeAbi.RuntimeValueFrameLeave);
        Assert.Equal("allocate", RuntimeAbi.RuntimeAllocate);
    }

    private sealed class MinimalLayoutProvider :
        ITargetLayout,
        IValueLayoutProvider,
        ITypeLayoutProvider,
        IInstanceFieldLayoutProvider,
        IStaticFieldLayoutProvider,
        IStaticDataLayout,
        IRuntimeObjectLayout,
        IManagedExceptionObjectProvider,
        ITypeDescriptorSource
    {
        public WasmTargetLayout Target => WasmTargetLayout.Wasm32;
        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            new(type, 4, 4, type.StackKind == CliValueKind.ManagedReference ? [0] : []);
        public ObjectLayout GetObjectLayout(CliTypeIdentity type) => new(1, 4, []);
        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
        {
            layout = GetObjectLayout(type);
            return true;
        }
        public ObjectLayout GetObjectLayout(EntityKey type) => new(1, 4, []);
        public FieldLayout GetFieldLayout(FieldInstanceModel field) => new(7);
        public FieldLayout GetFieldLayout(EntityKey field) => new(7);
        public StaticFieldLayout GetStaticFieldLayout(FieldInstanceModel field) => new(11);
        public StaticFieldLayout GetStaticFieldLayout(EntityKey field) => new(11);
        public StringLayout GetStringLayout(string value) => new(0, value.Length, 8);
        public int ReferenceArrayTypeId => 1;
        public int StringTypeId => 2;
        public int TypeTypeId => 3;
        public int DelegateTargetOffset => -1;
        public int DelegateMethodIdOffset => -1;
        public int DelegateLeftOffset => -1;
        public int DelegateRightOffset => -1;
        public int GetExceptionObject(ManagedExceptionKind kind) => 0;
        public int StringLengthOffset => 4;
        public int StringDataOffset => 8;
        public int ArrayLengthOffset => 4;
        public int ArrayDataPointerOffset => 8;
        public int ArrayElementTypeIdOffset => 12;
        public int StaticDataEnd => 16;
        public ImmutableArray<TypeDescriptorLayout> TypeDescriptors => [];
        public ImmutableArray<ConstructedTypeDescriptorLayout> ConstructedTypeDescriptors => [];
        public ImmutableArray<ValueTypeDescriptorLayout> ValueTypeDescriptors => [];
        public ImmutableArray<int> StaticRootAddresses => [];
        public ImmutableArray<DataSegment> DataSegments => [];
    }
}
