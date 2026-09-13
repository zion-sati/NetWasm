using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class TypeTestInstructionEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider typeLayouts,
    ICilTypeOperandResolver types,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions) :
    InstructionCommandProvider,
    ITypeTestEmitter
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.CastClass, (request, code) => Test(request, code, false)),
        Command(CilOperation.IsInstance, (request, code) => Test(request, code, true)),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) => new(
        operation,
        InstructionFamily.AllocationBoxingTypes,
        emit);

    public void Test(
        InstructionEmissionRequest request, IWasmInstructionWriter code,
        bool returnNullOnFailure)
    {
        var value = request.Stack.Count - 1;
        var valueLocal = GetStackLocal(
            request.Context,
            value,
            CliValueKind.ManagedReference);
        var caller = request.Header.MethodInstance?.CanonicalName;
        var key = caller is null
            ? string.Empty
            : $"{caller}@{request.Instruction.Offset:x8}";
        if (caller is null || !request.Target.TypeTestSites.TryGetValue(key, out var site))
        {
            EmitRuntimeTest(
                code,
                request.Instruction,
                request.Header.MethodInstance,
                valueLocal,
                returnNullOnFailure);
            return;
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        foreach (var matchingType in site.MatchingTypes)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(typeLayouts.GetObjectLayout(matchingType).TypeId)));
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Equal));
        }
        if (site.MatchingTypes.IsEmpty)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        }
        else
        {
            for (var index = 1; index < site.MatchingTypes.Length; index++)
            {
                code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32Or));
            }
        }
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitFailedTest(code, valueLocal, returnNullOnFailure);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitRuntimeTest(
        IWasmInstructionWriter code,
        CilInstruction instruction,
        MethodInstanceModel? method,
        int valueLocal,
        bool returnNullOnFailure)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        EmitReferenceEqualZero(code);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        var targetTypeId = instruction.Operand is CilOperand.Entity entity
            ? typeLayouts.GetObjectLayout(entity.Key).TypeId
            : typeLayouts.GetObjectLayout(types.Resolve(instruction, method)).TypeId;
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(targetTypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.IsAssignable)))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        EmitFailedTest(code, valueLocal, returnNullOnFailure);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitFailedTest(
        IWasmInstructionWriter code,
        int valueLocal,
        bool returnNullOnFailure)
    {
        if (returnNullOnFailure)
        {
            addresses.Emit(code, 0);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(valueLocal))));
        }
        else
        {
            exceptions.Emit(code, ManagedExceptionKind.InvalidCast);
        }
    }

    private void EmitReferenceEqualZero(IWasmInstructionWriter code)
    {
        addresses.Emit(code, AddressOperation.EqualZero);
    }

    private int GetStackLocal(
        MethodEmissionContext context,
        int slot,
        CliValueKind type) => WasmLocalLayoutPlanner.GetEvaluationStackLocal(
        context.StackLocals,
        slot,
        type,
        layouts.Target);
}
