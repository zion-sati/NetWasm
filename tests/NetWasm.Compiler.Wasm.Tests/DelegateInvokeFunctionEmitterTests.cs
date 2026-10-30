using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class DelegateInvokeFunctionEmitterTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void InitializesRequiredTargetsInsideTheirBranchAfterPublishingRoots(WasmTarget target, bool required)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var callbackType = CallbackType();
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var invoke = CreateInvoke(callbackType, signature);
        var method = CreateTarget(signature, "Target", true, Key(0x06000025), false);
        var writers = new RecordingWriterFactory();
        var initialization = new RecordingInitialization(required);
        var resolver = CreateResolver(callbackType, method);
        var targetProgram = CreateTargetProgram(invoke, method);

        ((IDelegateInvokeFunctionEmitter)CreateEmitter(layouts, writers: writers,
            initialization: initialization, triggers: initialization)).Emit(invoke, targetProgram, resolver);

        Assert.Same(method.Definition, initialization.Classified);
        if (required)
        {
            var request = Assert.Single(initialization.Requests);
            Assert.Equal(method.DeclaringType, request.DeclaringType);
            Assert.Equal(method.Definition.DeclaringType, request.TypeDefinition);
            Assert.Same(targetProgram.ModuleData, request.ModuleData);
            Assert.True(request.IsMethodInvocation);
            Assert.Same(resolver, initialization.FunctionIndices);
            // The target's branch publishes the delegate and arguments before initializing.
            var preceding = initialization.Preceding.ToList();
            var branch = preceding.FindLastIndex(instruction => instruction.Opcode == WasmOpcodes.If);
            var enter = preceding.FindLastIndex(instruction =>
                instruction.Opcode == WasmOpcodes.Call && instruction.Operand.UnsignedValue ==
                (uint)WasmRuntimeImports.CreateCatalog().Resolve(RuntimeImportSymbol.RootFrameEnter));
            Assert.True(branch >= 0 && enter > branch);
            Assert.Contains(initialization.Preceding, instruction =>
                instruction.Opcode == (target == WasmTarget.Wasm32 ? WasmOpcodes.I32Store : WasmOpcodes.I64Store));
        }
        else
            Assert.Empty(initialization.Requests);
    }

    // A single-cast delegate reads nothing after its target runs, so only the
    // multicast branch, or a target's initialization, roots the delegate and arguments.
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public void RootsOnlyForMulticastOrTargetInitialization(bool initializes, int frames)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(WasmTarget.Wasm32));
        var callbackType = CallbackType();
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var invoke = CreateInvoke(callbackType, signature);
        var method = CreateTarget(signature, "Target", true, Key(0x06000025), false);
        var writers = new RecordingWriterFactory();
        var initialization = new RecordingInitialization(initializes);

        ((IDelegateInvokeFunctionEmitter)CreateEmitter(layouts, writers: writers,
            initialization: initialization, triggers: initialization)).Emit(
                invoke, CreateTargetProgram(invoke, method), CreateResolver(callbackType, method));

        var enter = (uint)WasmRuntimeImports.CreateCatalog().Resolve(RuntimeImportSymbol.RootFrameEnter);
        Assert.Equal(frames, writers.Instructions.ToInstructions().Count(instruction =>
            instruction.Opcode == WasmOpcodes.Call && instruction.Operand.UnsignedValue == enter));
    }

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
            CreateTargetProgram(invoke, targetMethod), resolver);

        var instructions = writers.Instructions.ToInstructions();
        var targetCall = WasmInstruction.WithOperand(WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)resolver.Resolve(targetMethod)));
        var callIndex = Assert.Single(Enumerable.Range(0, instructions.Length),
            index => instructions[index] == targetCall);
        if (isStatic)
        {
            // A static target takes no receiver from the delegate.
            var branch = Array.FindLastIndex(instructions[..callIndex].ToArray(),
                instruction => instruction.Opcode == WasmOpcodes.If);
            Assert.DoesNotContain(instructions[branch..callIndex], instruction =>
                instruction.Operand.Shape == WasmInstructionOperandShape.MemoryArgument &&
                instruction.Operand.Offset == (uint)layouts.DelegateTargetOffset);
            Assert.Null(values.RequestedType);
            return;
        }

        var expected = new List<WasmInstruction>
        {
            WasmInstruction.WithOperand(WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned((uint)(
                    new ManagedMethodFunctionTypeResolver().Resolve(invoke).Parameters.Length + 5))),
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

    // Past four targets the stub reads the method id once, then jumps through a
    // branch table when the targets' function indices are dense and otherwise
    // finds the target by binary search, instead of testing each in turn.
    [Theory]
    [InlineData(4, 1, 0, false)]
    [InlineData(9, 1, 0, true)]
    [InlineData(17, 2, 0, true)]
    [InlineData(9, 25, 2, false)]
    [InlineData(17, 13, 4, false)]
    public void FindsManyTargetsByFunctionIndex(int count, int spacing, int searchSteps, bool table)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(WasmTarget.Wasm32));
        var callbackType = CallbackType();
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var invoke = CreateInvoke(callbackType, signature);
        var methods = Enumerable.Range(0, count)
            .Select(index => CreateTarget(signature, $"Target{index}", true, Key(0x06000030 + index * spacing)))
            .ToArray();
        var writers = new RecordingWriterFactory();
        var resolver = CreateResolver(callbackType, methods);

        ((IDelegateInvokeFunctionEmitter)CreateEmitter(layouts, writers: writers)).Emit(
            invoke, CreateTargetProgram(invoke, methods), resolver);

        var instructions = writers.Instructions.ToInstructions();
        var currentDelegate = WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned(
            (uint)(new ManagedMethodFunctionTypeResolver().Resolve(invoke).Parameters.Length + 5)));
        var methodId = WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)layouts.DelegateMethodIdOffset));
        Assert.Equal(count > 4 ? 1 : count, Enumerable.Range(1, instructions.Length - 1).Count(index =>
            instructions[index] == methodId && instructions[index - 1] == currentDelegate));
        Assert.Equal(searchSteps, instructions.Count(instruction =>
            instruction.Opcode == WasmOpcodes.I32LessThanUnsigned));
        var indices = methods.Select(method => resolver.Resolve(method)).Order().ToArray();
        if (table)
        {
            var branchTable = Assert.Single(instructions, instruction => instruction.Opcode == WasmOpcodes.BranchTable);
            // Each index in the table's range names its target's block; gaps leave by the default.
            Assert.Equal(0, instructions.Count(instruction => instruction.Opcode == WasmOpcodes.I32Equal));
            Assert.Equal(
                string.Join(",", Enumerable.Range(indices[0], indices[^1] - indices[0] + 1).Select(index =>
                    Array.IndexOf(indices, index) is var position and >= 0 ? (uint)position : (uint)count)),
                string.Join(",", branchTable.Operand.BranchTableTargets));
            Assert.Equal((uint)count, branchTable.Operand.BranchTableDefault);
        }
        else
        {
            Assert.DoesNotContain(instructions, instruction => instruction.Opcode == WasmOpcodes.BranchTable);
            Assert.Equal(count, instructions.Count(instruction => instruction.Opcode == WasmOpcodes.I32Equal));
        }
        Assert.All(methods, method => Assert.Single(instructions, instruction =>
            instruction == WasmInstruction.WithOperand(WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)resolver.Resolve(method)))));
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
            [])
        {
            ManagedThrowFunctionIndex = OptionalFunctionIndex.At(EmitterTestSupport.ManagedThrowFunction),
        };
        var emitter = new DelegateInvokeFunctionEmitter(layouts, layouts, layouts, WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts, layouts),
            new ManagedMethodFunctionTypeResolver(),
            new DelegateInvocationTargetSelector(),
            new GeneratedFunctionWriterFactory(),
            new StaticInitializationEmitter(program, program, new AddressInstructionEmitter(layouts)),
            new MethodInitializationTriggerClassifier(program));

        var function = ((IDelegateInvokeFunctionEmitter)emitter).Emit(
            invoke,
            new DelegateInvokeTarget(
                [CreateBinding(invoke, target)], ModuleDataPlan.Empty),
            new FunctionIndexResolver(program, program, indices));

        Assert.NotEmpty(function);
        Assert.Contains(WasmOpcodes.Call, function);
        Assert.True(ContainsManagedThrow(function));
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
            CreateTargetProgram(invoke, valueTarget),
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
        var writers = new RecordingWriterFactory();
        var emitter = CreateEmitter(layouts, writers: writers);
        var indices = CreateResolver(
            callbackType,
            staticTarget,
            instanceTarget,
            incompatibleTarget);

        var function = ((IDelegateInvokeFunctionEmitter)emitter).Emit(
            invoke,
            CreateTargetProgram(
                invoke,
                staticTarget,
                instanceTarget),
            indices);

        Assert.NotEmpty(function);
        var instructions = writers.Instructions.ToInstructions();
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.I32Load);
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.I32Store);
        Assert.Contains(instructions, instruction => instruction.Opcode == WasmOpcodes.I32Add);

        var parameterCount = new ManagedMethodFunctionTypeResolver()
            .Resolve(invoke)
            .Parameters
            .Length;
        var invocationListLocal = checked((uint)(parameterCount + 2));
        var currentDelegateLocal = checked((uint)(parameterCount + 5));
        var selectionIf = Assert.Single(Enumerable.Range(2, instructions.Length - 2), index =>
            instructions[index].Opcode == WasmOpcodes.If &&
            instructions[index - 1].Opcode == WasmOpcodes.I32EqualZero &&
            instructions[index - 2] == WasmInstruction.WithOperand(
                WasmOpcodes.LocalGet,
                WasmInstructionOperand.Unsigned(invocationListLocal)));
        var selectionElse = Enumerable.Range(
                selectionIf + 1,
                instructions.Length - selectionIf - 1)
            .First(index => instructions[index].Opcode == WasmOpcodes.Else);
        var selectionEnd = Enumerable.Range(
                selectionElse + 1,
                instructions.Length - selectionElse - 1)
            .First(index => instructions[index].Opcode == WasmOpcodes.End);
        var storeCurrentDelegate = WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned(currentDelegateLocal));
        var loadCurrentDelegate = WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(currentDelegateLocal));

        Assert.True(selectionElse > selectionIf);
        Assert.True(selectionEnd > selectionElse);
        Assert.Equal(storeCurrentDelegate, instructions[selectionElse - 1]);
        Assert.Equal(storeCurrentDelegate, instructions[selectionEnd - 1]);
        Assert.Equal(loadCurrentDelegate, instructions[selectionEnd + 1]);
    }

    private static DelegateInvokeFunctionEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IValueLayoutProvider? values = null,
        IGeneratedFunctionWriterFactory? writers = null,
        IStaticInitializationEmitter? initialization = null,
        IMethodInitializationTriggerClassifier? triggers = null) =>
        new DelegateInvokeFunctionEmitter(
            layouts,
            values ?? layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts, layouts),
            new ManagedMethodFunctionTypeResolver(),
            new DelegateInvocationTargetSelector(),
            writers ?? new GeneratedFunctionWriterFactory(),
            initialization ?? new StaticInitializationEmitter(new FakeProgram(), new FakeProgram(), new AddressInstructionEmitter(layouts)),
            triggers ?? new MethodInitializationTriggerClassifier(new FakeProgram()));

    private sealed class RecordingInitialization(bool required) : IStaticInitializationEmitter,
        IMethodInitializationTriggerClassifier
    {
        public MethodDefinitionModel? Classified { get; private set; }
        public List<StaticInitializationEmissionRequest> Requests { get; } = [];
        public ImmutableArray<WasmInstruction> Preceding { get; private set; } = [];
        public IFunctionIndexResolver? FunctionIndices { get; private set; }

        public bool Classify(MethodDefinitionModel method)
        {
            Classified = method;
            return required;
        }

        public void Emit(StaticInitializationEmissionRequest request, IWasmInstructionWriter code,
            IFunctionIndexResolver functionIndices)
        {
            Requests.Add(request);
            FunctionIndices = functionIndices;
            Preceding = ((RecordingInstructionWriter)code).ToInstructions();
        }
    }

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
        MethodInstanceModel invoke,
        params MethodInstanceModel[] targets) =>
        new(
            [.. targets.Select(target => CreateBinding(invoke, target))], ModuleDataPlan.Empty);

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
        [])
        {
            ManagedThrowFunctionIndex = OptionalFunctionIndex.At(EmitterTestSupport.ManagedThrowFunction),
        };

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
