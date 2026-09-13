using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Instructions;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class DelegateInvokeCallEmitterTests
{
    [Fact]
    public void EmitsDelegateHelperCallAndProducesScalarResult()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider();
        var delegateType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Callback",
            isValueType: false);
        var methodDefinition = program.GetMethod(EntryKey) with
        {
            Name = "Invoke",
            IsStatic = false,
            Signature = MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.I4),
        };
        var method = new MethodInstanceModel(
            methodDefinition,
            delegateType,
            [],
            methodDefinition.Signature);
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            [CliValueKind.ManagedReference, CliValueKind.I4]) with
        {
            Target = CreateInstructionModuleTarget(program) with
            {
                FunctionIndices = new FunctionIndexMap(
                    [],
                    [],
                    ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                        delegateType.CanonicalName,
                        new(32)),
                    []),
            },
        };
        var code = new RecordingInstructionWriter();
        var emitter = new DelegateInvokeCallEmitter(
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7));

        EmitCall(
            emitter,
            new CallEmissionRequest(instruction, method, 0, 2),
            code,
            CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.I4], instruction.Stack);
        Assert.Contains(WasmOpcodes.Call, code.ToArray());
    }

    [Fact]
    public void EmitsVoidDelegateCallUsingMemory64AndLeavesNoResult()
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.Wasm64);
        var delegateType = CallbackType();
        var method = CreateDelegateMethod(
            delegateType,
            MethodSignatureModel.Create(CliValueKind.Void));
        var instruction = CreateDelegateInstruction(
            program,
            delegateType,
            [CliValueKind.ManagedReference]);
        var code = new RecordingInstructionWriter();
        var emitter = new DelegateInvokeCallEmitter(
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7));

        EmitCall(
            emitter,
            new CallEmissionRequest(instruction, method, 0, 1),
            code,
            CreateFunctionIndexResolver(program));

        Assert.Empty(instruction.Stack);
        Assert.Contains(WasmOpcodes.I64EqualZero, code.ToArray());
    }

    [Theory]
    [InlineData(WasmTarget.Wasm32, 0, WasmOpcodes.I32Constant)]
    [InlineData(WasmTarget.Wasm32, 8, WasmOpcodes.I32Constant)]
    [InlineData(WasmTarget.Wasm64, 8, WasmOpcodes.I64Constant)]
    public void EmitsValueTypeDelegateCallForFrameOffsetAndMemoryWidth(
        WasmTarget target,
        int returnOffset,
        byte expectedConstant)
    {
        var program = new FakeProgram();
        var layouts = new RecordingLayoutProvider(WasmTargetLayout.For(target));
        var delegateType = CallbackType();
        var valueType = CliTypeIdentity.Named(
            Assembly,
            "Test",
            "Value",
            isValueType: true);
        var method = CreateDelegateMethod(
            delegateType,
            MethodSignatureModel.Create(valueType));
        var instruction = CreateDelegateInstruction(
            program,
            delegateType,
            [CliValueKind.ManagedReference],
            new ValueFrameLayout(
                16,
                [],
                [],
                ImmutableDictionary<int, int>.Empty.Add(0, returnOffset),
                []));
        var code = new InstructionRecordingWriter();
        var emitter = new DelegateInvokeCallEmitter(
            layouts,
            new ImplicitExceptionEmitter(layouts, layouts, 7));

        EmitCall(
            emitter,
            new CallEmissionRequest(instruction, method, 0, 1),
            code,
            CreateFunctionIndexResolver(program));

        Assert.Equal([CliValueKind.ValueType], instruction.Stack);
        Assert.Contains(
            code.Instructions,
            instruction => instruction.Opcode == WasmOpcodes.Call);
        Assert.Equal(
            returnOffset == 0 ? 0 : 2,
            code.Instructions.Count(instruction =>
                instruction.Opcode == expectedConstant &&
                (target == WasmTarget.Wasm64
                    ? instruction.Operand.Signed64Value == returnOffset
                    : instruction.Operand.SignedValue == returnOffset)));
    }

    private static void EmitCall<TEmitter>(
        TEmitter emitter,
        CallEmissionRequest request,
        IWasmInstructionWriter code,
        IFunctionIndexResolver functionIndices)
        where TEmitter : ICallEmitter => emitter.Emit(request, code, functionIndices);

    private static CliTypeIdentity CallbackType() =>
        CliTypeIdentity.Named(Assembly, "Test", "Callback", isValueType: false);

    private static MethodInstanceModel CreateDelegateMethod(
        CliTypeIdentity delegateType,
        MethodSignatureModel signature)
    {
        var definition = new MethodDefinitionModel(
            EntryKey,
            TypeKey,
            "Invoke",
            false,
            signature,
            1);
        return new(definition, delegateType, [], signature);
    }

    private sealed class InstructionRecordingWriter : IWasmInstructionWriter
    {
        private readonly List<WasmInstruction> _instructions = [];

        public IReadOnlyList<WasmInstruction> Instructions => _instructions;

        public void Write(WasmInstruction instruction)
        {
            ArgumentNullException.ThrowIfNull(instruction);
            _instructions.Add(instruction);
        }
    }

    private static InstructionEmissionRequest CreateDelegateInstruction(
        FakeProgram program,
        CliTypeIdentity delegateType,
        IEnumerable<CliValueKind> stack,
        ValueFrameLayout? valueLayout = null)
    {
        var context = CreateMethodEmissionContext() with
        {
            ValueLayout = valueLayout ?? CreateMethodEmissionContext().ValueLayout,
        };
        var instruction = CreateInstructionRequest(
            CilOperation.Call,
            stack,
            context: context) with
        {
            Target = CreateInstructionModuleTarget(program) with
            {
                FunctionIndices = new FunctionIndexMap(
                    [],
                    [],
                    ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                        delegateType.CanonicalName,
                        new(32)),
                    []),
            },
        };
        return instruction;
    }

    [Fact]
    public void RejectsAnOrdinaryInstanceCall()
    {
        var program = new FakeProgram();
        var resolver = new CallEmissionKindResolver(program, new FakeIntrinsics());
        var method = program.GetMethod(ConstructorKey);
        var instance = new MethodInstanceModel(
            method,
            CliTypeIdentity.Named(
                Assembly,
                "Test",
                "Type",
                isValueType: false),
            [],
            method.Signature);

        Assert.Equal(
            CallEmissionKind.Direct,
            resolver.Resolve(new(
                CreateInstructionRequest(CilOperation.Call),
                instance,
                0,
                1),
                new RecordingInstructionWriter()));
    }
}
