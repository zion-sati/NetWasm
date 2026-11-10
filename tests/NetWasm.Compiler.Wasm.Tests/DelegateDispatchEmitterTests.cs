using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static DelegateEmitterTestSupport;
using static EmitterTestSupport;

public sealed class DelegateDispatchEmitterTests
{
    private const int DelegateLocal = 9;
    private const int ResultLocal = 5;
    private const int InitializationFrameLocal = 6;
    private const int MethodIdLocal = 7;

    // A type initializer may allocate before the call reads the delegate and the arguments,
    // so the target's branch roots them in the initialization frame, initializes, and
    // leaves the frame before the call.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootsAroundARequiredTypeInitializerInsideItsTargetsBranch(bool required)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(WasmTarget.Wasm32));
        var callbackType = CallbackType();
        var signature = MethodSignatureModel.Create(CliValueKind.I4);
        var invoke = CreateInvoke(callbackType, signature);
        var method = CreateTarget(signature, "Target", true, Key(0x06000025));
        var initialization = new RecordingInitialization(required);
        var roots = new RecordingRoots();
        var resolver = CreateResolver(callbackType, method);
        var program = CreateTargetProgram(invoke, method);
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(layouts, initialization: initialization, roots: roots), code,
            Dispatch(invoke, program, resolver));

        Assert.Same(method.Definition, initialization.Classified);
        var instructions = code.ToInstructions();
        var call = Assert.Single(Enumerable.Range(0, instructions.Length), index =>
            instructions[index] == Call(resolver.Resolve(method)));
        if (!required)
        {
            Assert.Empty(initialization.Requests);
            Assert.Empty(roots.Requests);
            return;
        }
        var request = Assert.Single(initialization.Requests);
        Assert.Equal(method.DeclaringType, request.DeclaringType);
        Assert.Equal(method.Definition.DeclaringType, request.TypeDefinition);
        Assert.Same(program.StaticInitializerGuards, request.Guards);
        Assert.True(request.IsMethodInvocation);
        Assert.Same(resolver, initialization.FunctionIndices);
        Assert.Equal((invoke, InitializationFrameLocal), Assert.Single(roots.Requests));
        // The target's branch roots, then initializes, then leaves the frame before the call.
        var preceding = initialization.Preceding;
        var branch = preceding.ToList().FindLastIndex(instruction => instruction.Opcode == WasmOpcodes.If);
        Assert.Equal(WasmOpcodes.Drop, preceding[^1].Opcode);
        Assert.True(branch >= 0 && branch < preceding.Length - 1);
        Assert.Equal(
            [LocalGet(InitializationFrameLocal), Call(RuntimeImport(RuntimeImportSymbol.RootFrameLeave))],
            instructions[preceding.Length..(preceding.Length + 2)]);
        Assert.True(call > preceding.Length + 1);
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
        var resolver = CreateResolver(callbackType, targetMethod);
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(layouts, values), code,
            Dispatch(invoke, CreateTargetProgram(invoke, targetMethod), resolver));

        var instructions = code.ToInstructions();
        var callIndex = Assert.Single(Enumerable.Range(0, instructions.Length),
            index => instructions[index] == Call(resolver.Resolve(targetMethod)));
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
            LocalGet(DelegateLocal),
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

    // A scalar result is kept in the result local; a value-type result is written through
    // the hidden first parameter, which the target takes first, before the arguments that
    // then start at the third parameter; a void target leaves nothing.
    [Theory]
    [InlineData(CliValueKind.I8, false)]
    [InlineData(CliValueKind.Void, false)]
    [InlineData(CliValueKind.Void, true)]
    public void PassesTheResultAndArgumentsAsTheInvokeReturnsThem(CliValueKind result, bool valueResult)
    {
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(WasmTarget.Wasm32));
        var callbackType = CallbackType();
        var argument = CliTypeIdentity.FromStackKind(CliValueKind.I4);
        var invoke = valueResult
            ? CreateInvoke(callbackType, CliTypeIdentity.Named(Assembly, "Test", "Result", isValueType: true), argument)
            : CreateInvoke(callbackType, CliTypeIdentity.FromStackKind(result), argument);
        var targetMethod = CreateTarget(invoke.Signature, "Target", isStatic: true, Key(0x06000025));
        var resolver = CreateResolver(callbackType, targetMethod);
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(layouts), code,
            Dispatch(invoke, CreateTargetProgram(invoke, targetMethod), resolver));

        var instructions = code.ToInstructions();
        var callIndex = Assert.Single(Enumerable.Range(0, instructions.Length),
            index => instructions[index] == Call(resolver.Resolve(targetMethod)));
        var expected = valueResult
            ? new[] { LocalGet(0), LocalGet(2) }
            : [LocalGet(1)];
        Assert.Equal(expected, instructions[(callIndex - expected.Length)..callIndex]);
        Assert.Equal(result == CliValueKind.I8 ? 1 : 0,
            instructions.Count(instruction => instruction == LocalSet(ResultLocal)));
        Assert.Equal(result == CliValueKind.I8, instructions[callIndex + 1] == LocalSet(ResultLocal));
    }

    // Past four targets the dispatch reads the method id once, then jumps through a branch
    // table when the targets' function indices are dense and otherwise finds the target by
    // binary search, instead of testing each in turn.
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
        var resolver = CreateResolver(callbackType, methods);
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(layouts), code, Dispatch(invoke, CreateTargetProgram(invoke, methods), resolver));

        var instructions = code.ToInstructions();
        var methodId = WasmInstruction.WithOperand(WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, (uint)layouts.DelegateMethodIdOffset));
        Assert.Equal(count > 4 ? 1 : count, Enumerable.Range(1, instructions.Length - 1).Count(index =>
            instructions[index] == methodId && instructions[index - 1] == LocalGet(DelegateLocal)));
        Assert.Equal(count > 4 ? 1 : 0, instructions.Count(instruction => instruction == LocalSet(MethodIdLocal)));
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
            instruction == Call(resolver.Resolve(method))));
        // An index no target has throws InvalidCastException.
        Assert.True(ContainsManagedThrow(instructions));
    }

    // Only the targets bound to this delegate type's Invoke are dispatched to.
    [Fact]
    public void DispatchesOnlyToTheTargetsBoundToThisInvoke()
    {
        var layouts = new RecordingLayoutProvider();
        var callbackType = CallbackType();
        var signature = MethodSignatureModel.Create(
            CliValueKind.Void,
            CliValueKind.ManagedReference,
            CliValueKind.I4);
        var invoke = CreateInvoke(callbackType, signature);
        var otherInvoke = CreateInvoke(CliTypeIdentity.Named(Assembly, "Test", "Other", isValueType: false), signature);
        var staticTarget = CreateTarget(signature, "StaticTarget", isStatic: true, Key(0x06000022));
        var instanceTarget = CreateTarget(signature, "InstanceTarget", isStatic: false, Key(0x06000023));
        var otherTarget = CreateTarget(signature, "OtherTarget", isStatic: true, Key(0x06000024));
        var resolver = CreateResolver(callbackType, staticTarget, instanceTarget, otherTarget);
        var code = new RecordingInstructionWriter();

        Emit(CreateEmitter(layouts), code, Dispatch(invoke,
            new([CreateBinding(invoke, staticTarget), CreateBinding(invoke, instanceTarget),
                CreateBinding(otherInvoke, otherTarget)], Lookups.NoEntries<string, StaticInitializerGuard>()),
            resolver));

        var instructions = code.ToInstructions();
        Assert.Single(instructions, instruction => instruction == Call(resolver.Resolve(staticTarget)));
        Assert.Single(instructions, instruction => instruction == Call(resolver.Resolve(instanceTarget)));
        Assert.DoesNotContain(instructions, instruction => instruction == Call(resolver.Resolve(otherTarget)));
        Assert.Equal(2, instructions.Count(instruction => instruction.Opcode == WasmOpcodes.I32Equal));
    }

    private static (DelegateDispatch Dispatch, IFunctionIndexResolver FunctionIndices) Dispatch(
        MethodInstanceModel invoke, DelegateInvokeTarget program, IFunctionIndexResolver resolver) =>
        (new(invoke, program, DelegateLocal, ResultLocal, InitializationFrameLocal, MethodIdLocal), resolver);

    private static void Emit(DelegateDispatchEmitter emitter, IWasmInstructionWriter code,
        (DelegateDispatch Dispatch, IFunctionIndexResolver FunctionIndices) request) =>
        ((IDelegateDispatchEmitter)emitter).Emit(code, request.Dispatch, request.FunctionIndices);

    private static DelegateDispatchEmitter CreateEmitter(
        RecordingLayoutProvider layouts,
        IValueLayoutProvider? values = null,
        RecordingInitialization? initialization = null,
        IDelegateArgumentRootEmitter? roots = null) =>
        new(
            layouts,
            values ?? layouts,
            layouts,
            WasmRuntimeImports.CreateCatalog(),
            new ImplicitExceptionEmitter(layouts),
            new DelegateInvocationTargetSelector(),
            initialization ?? (IStaticInitializationEmitter)new StaticInitializationEmitter(
                new StaticInitializerGuardResolver(new FakeProgram(), new FakeProgram()), new AddressInstructionEmitter(layouts)),
            initialization ?? (IMethodInitializationTriggerClassifier)new MethodInitializationTriggerClassifier(new FakeProgram()),
            roots ?? new RecordingRoots());

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
}
