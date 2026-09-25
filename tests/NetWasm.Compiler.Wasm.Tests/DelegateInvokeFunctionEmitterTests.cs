using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class DelegateInvokeFunctionEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false, false, 4)]
    [InlineData(WasmTarget.Wasm64, false, false, 4)]
    [InlineData(WasmTarget.Wasm32, true, false, 4)]
    [InlineData(WasmTarget.Wasm64, true, false, 4)]
    [InlineData(WasmTarget.Wasm32, false, true, 4)]
    [InlineData(WasmTarget.Wasm64, false, true, 4)]
    [InlineData(WasmTarget.Wasm32, false, true, 16)]
    [InlineData(WasmTarget.Wasm64, false, true, 16)]
    public void PassesTheStoredObjectOrAlignedBoxedPayloadToTheTarget(
        WasmTarget target, bool isStatic, bool isValueType, int alignment)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var callbackType = CallbackType();
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var invoke = CreateInvoke(callbackType, signature);
        var targetMethod = CreateTarget(signature, "Receiver", isStatic,
            Key(0x06000025), isValueType);
        var values = new FixedValueLayoutProvider(alignment);
        var writers = new RecordingWriterFactory();
        var emitter = CreateEmitter(layouts, values, writers);
        var resolver = CreateResolver(callbackType, targetMethod);

        ((IDelegateInvokeFunctionEmitter)emitter).Emit(invoke,
            CreateTargetProgram(callbackType, invoke, targetMethod), resolver);

        var instructions = writers.Instructions.ToInstructions();
        var targetCall = WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)resolver.Resolve(targetMethod)));
        var callIndex = Assert.Single(Enumerable.Range(0, instructions.Length),
            index => instructions[index] == targetCall);
        if (isStatic)
        {
            Assert.Equal(WasmOpcodes.If, instructions[callIndex - 1].Opcode);
            Assert.Null(values.RequestedType);
            return;
        }

        var expected = new List<WasmInstruction>
        {
            WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned(0)),
            WasmInstruction.WithOperand(layouts.Target.UsesMemory64
                    ? WasmOpcodes.I64Load : WasmOpcodes.I32Load,
                WasmInstructionOperand.Memory(layouts.Target.UsesMemory64 ? 3u : 2u,
                    (uint)layouts.DelegateTargetOffset)),
        };
        if (isValueType)
        {
            var payloadOffset = alignment == 16 ? 16 : layouts.Target.ObjectHeaderSize;
            expected.Add(WasmInstruction.WithOperand(layouts.Target.UsesMemory64
                    ? WasmOpcodes.I64Constant : WasmOpcodes.I32Constant,
                layouts.Target.UsesMemory64
                    ? WasmInstructionOperand.Signed64(payloadOffset)
                    : WasmInstructionOperand.Signed(payloadOffset)));
            expected.Add(WasmInstruction.NoOperand(layouts.Target.UsesMemory64
                ? WasmOpcodes.I64Add : WasmOpcodes.I32Add));
            Assert.Equal(targetMethod.DeclaringType, values.RequestedType);
        }
        else
        {
            Assert.Null(values.RequestedType);
        }
        Assert.Equal(expected, instructions.Skip(callIndex - expected.Count).Take(expected.Count));
    }

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
        IValueLayoutProvider? values = null,
        IGeneratedFunctionWriterFactory? writers = null) =>
        new DelegateInvokeFunctionEmitter(
            layouts,
            values ?? layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts, layouts, 7),
            new ExceptionPayloadBlockEmitter(layouts),
            new ManagedMethodFunctionTypeResolver(),
            new DelegateInvocationTargetSelector(),
            writers ?? new GeneratedFunctionWriterFactory());

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
        EntityKey key,
        bool isValueType = false)
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
            CliTypeIdentity.Named(Assembly, "Test", name, isValueType),
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

    private sealed class FixedValueLayoutProvider(int alignment) : IValueLayoutProvider
    {
        public CliTypeIdentity? RequestedType { get; private set; }

        public ValueLayout GetValueLayout(CliTypeIdentity type)
        {
            RequestedType = type;
            return new(type, alignment, alignment, []);
        }
    }

    private sealed class RecordingWriterFactory : IGeneratedFunctionWriterFactory
    {
        public RecordingInstructionWriter Instructions { get; } = new();

        public GeneratedFunctionWriterLease Create()
        {
            var buffer = new WasmBinaryBuffer();
            return new(new WasmBinaryWriter(buffer), new WasmBinarySnapshotReader(buffer), Instructions);
        }
    }
}
