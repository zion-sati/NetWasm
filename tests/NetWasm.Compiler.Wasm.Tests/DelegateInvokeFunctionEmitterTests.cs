using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class DelegateInvokeFunctionEmitterTests
{
    [Fact]
    public void EmitsRootedCheckedDelegateDispatchFunction()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var callbackType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Callback",
            isValueType: false);
        var invokeDefinition = new MethodDefinitionModel(
            Key(0x06000010),
            TypeKey,
            "Invoke",
            false,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
            0);
        var invoke = new MethodInstanceModel(
            invokeDefinition,
            callbackType,
            [],
            invokeDefinition.Signature);
        var targetMethod = program.GetMethod(EntryKey);
        var target = new MethodInstanceModel(
            targetMethod,
            CliTypeIdentity.Named(Assembly, "Test", "Type", isValueType: false),
            [],
            targetMethod.Signature);
        var indices = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey,
                new WasmFunctionIndex(30)),
            [],
            ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                callbackType.CanonicalName,
                new WasmFunctionIndex(40)),
            []);
        var emitter = new DelegateInvokeFunctionEmitter(layouts, layouts, layouts, WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            new ExceptionPayloadBlockEmitter(layouts),
            new ManagedMethodFunctionTypeResolver(),
            new DelegateInvocationTargetSelector(),
            new GeneratedFunctionWriterFactory());

        var function = ((IDelegateInvokeFunctionEmitter)emitter).Emit(
            invoke,
            new DelegateInvokeTarget(
                indices,
                [CreateBinding(invoke, target)]),
            new FunctionIndexResolver(program, program, indices));

        Assert.NotEmpty(function);
        Assert.Contains(WasmOpcodes.TryTable, function);
        Assert.Contains(WasmOpcodes.Call, function);
        Assert.Contains(WasmOpcodes.Throw, function);
    }

    [Fact]
    public void EmitsValueReturningMemory64DelegateWithAllReferenceRootShapes()
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var callbackType = CallbackType();
        var valueWithReferences = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "ValueWithReferences",
            isValueType: true);
        var valueWithoutReferences = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "ValueWithoutReferences",
            isValueType: true);
        var invoke = CreateInvoke(
            callbackType,
            CliTypeIdentity.Named(Assembly, "Test", "Result", isValueType: true),
            valueWithReferences,
            valueWithoutReferences,
            CliTypeIdentity.FromStackKind(CliValueKind.ManagedReference));
        var valueTarget = CreateTarget(
            invoke.Signature,
            "ValueTarget",
            isStatic: true,
            Key(0x06000021));
        var emitter = CreateEmitter(
            layouts,
            new ReferenceValueLayoutProvider(valueWithReferences));

        var function = ((IDelegateInvokeFunctionEmitter)emitter).Emit(
            invoke,
            CreateTargetProgram(callbackType, invoke, valueTarget),
            CreateResolver(callbackType, valueTarget));

        Assert.NotEmpty(function);
        Assert.Contains(WasmOpcodes.I64Load, function);
        Assert.Contains(WasmOpcodes.I64Store, function);
        Assert.Contains(WasmOpcodes.I64Add, function);
        Assert.Contains(WasmOpcodes.I64EqualZero, function);
    }

    [Fact]
    public void EmitsVoidDelegateWithStaticAndInstanceCompatibleTargets()
    {
        var layouts = new RecordingLayoutProvider();
        var callbackType = CallbackType();
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.ManagedReference,
            CliValueKind.I4);
        var invoke = CreateInvoke(callbackType, signature);
        var staticTarget = CreateTarget(
            signature,
            "StaticTarget",
            isStatic: true,
            Key(0x06000022));
        var instanceTarget = CreateTarget(
            signature,
            "InstanceTarget",
            isStatic: false,
            Key(0x06000023));
        var incompatibleTarget = CreateTarget(
            MethodSignatureModel.Create(CliValueKind.Void, CliValueKind.I8),
            "IncompatibleTarget",
            isStatic: true,
            Key(0x06000024));
        var emitter = CreateEmitter(layouts);
        var indices = CreateResolver(
            callbackType,
            staticTarget,
            instanceTarget,
            incompatibleTarget);

        var function = ((IDelegateInvokeFunctionEmitter)emitter).Emit(
            invoke,
            CreateTargetProgram(
                callbackType,
                invoke,
                staticTarget,
                instanceTarget),
            indices);

        Assert.NotEmpty(function);
        Assert.Contains(WasmOpcodes.I32Load, function);
        Assert.Contains(WasmOpcodes.I32Store, function);
        Assert.Contains(WasmOpcodes.I32Add, function);
    }

    private static DelegateInvokeFunctionEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IValueLayoutProvider? values = null) =>
        new DelegateInvokeFunctionEmitter(
            layouts,
            values ?? layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            new ExceptionPayloadBlockEmitter(layouts),
            new ManagedMethodFunctionTypeResolver(),
            new DelegateInvocationTargetSelector(),
            new GeneratedFunctionWriterFactory());

    private static CliTypeIdentity CallbackType() =>
        CliTypeIdentity.Named(Assembly, "Test", "Callback", isValueType: false);

    private static MethodInstanceModel CreateInvoke(
        CliTypeIdentity callbackType,
        CliTypeIdentity returnType,
        params CliTypeIdentity[] parameters)
    {
        var definition = new MethodDefinitionModel(
            Key(0x06000020),
            TypeKey,
            "Invoke",
            false,
            new MethodSignatureModel(returnType, [.. parameters]),
            1);
        return new(definition, callbackType, [], definition.Signature);
    }

    private static MethodInstanceModel CreateInvoke(
        CliTypeIdentity callbackType,
        MethodSignatureModel signature) =>
        CreateInvoke(callbackType, signature.ReturnSignatureType,
            [.. signature.ParameterSignatureTypes]);

    private static MethodInstanceModel CreateTarget(
        MethodSignatureModel signature,
        string name,
        bool isStatic,
        EntityKey key)
    {
        var definition = new MethodDefinitionModel(
            key,
            TypeKey,
            name,
            isStatic,
            signature,
            1);
        return new(
            definition,
            CliTypeIdentity.Named(Assembly, "Test", name, isValueType: false),
            [],
            signature);
    }

    private static DelegateInvokeTarget CreateTargetProgram(
        CliTypeIdentity callbackType,
        MethodInstanceModel invoke,
        params MethodInstanceModel[] targets) =>
        new(
            CreateIndices(callbackType, targets),
            [.. targets.Select(target => CreateBinding(invoke, target))]);

    private static FunctionIndexResolver CreateResolver(
        CliTypeIdentity callbackType,
        params MethodInstanceModel[] targets)
    {
        var program = new FakeProgram();
        return new FunctionIndexResolver(
            program,
            program,
            CreateIndices(callbackType, targets));
    }

    private static FunctionIndexMap CreateIndices(
        CliTypeIdentity callbackType,
        params MethodInstanceModel[] targets) => new(
        targets.ToImmutableDictionary(
            target => target.Definition.Key,
            target => new WasmFunctionIndex(
                (50 + target.Definition.Key.MetadataToken) & 0xff)),
        [],
        ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
            callbackType.CanonicalName,
            new WasmFunctionIndex(40)),
        []);

    private static ManagedDelegateBinding CreateBinding(
        MethodInstanceModel invoke,
        MethodInstanceModel target) => new(
        new ManagedMethodIdentity(invoke.CanonicalName),
        new ManagedMethodIdentity(target.CanonicalName),
        invoke,
        target,
        [.. invoke.Signature.ParameterSignatureTypes.Select((parameter, index) =>
            new ManagedDelegateValueBinding(
                parameter,
                target.Signature.ParameterSignatureTypes[index],
                ManagedDelegateAdaptation.Identity))],
        new ManagedDelegateValueBinding(
            target.Signature.ReturnSignatureType,
            invoke.Signature.ReturnSignatureType,
            ManagedDelegateAdaptation.Identity));

    private sealed class ReferenceValueLayoutProvider(
        CliTypeIdentity referencedType) : IValueLayoutProvider
    {
        public ValueLayout GetValueLayout(CliTypeIdentity type) =>
            type.Equals(referencedType)
                ? new(type, 16, 8, [0, 8])
                : new(type, 8, 8, []);
    }
}
