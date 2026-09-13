using NetWasm.Compiler.Wasm.Encoding;
using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Wasm.Emission.Methods;
using NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;
using NetWasm.Compiler.Wasm.Emission.Support;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal sealed class BoxingInstructionEmitter(
    ITargetLayout layouts,
    IAddressInstructionEmitter addresses,
    ITypeLayoutProvider typeLayouts,
    IValueLayoutProvider values,
    ICilTypeOperandResolver types,
    IRuntimeImportResolver runtimeImports,
    IImplicitExceptionEmitter exceptions,
    IRootPublicationEmitter roots,
    INullableTypeResolver nullableTypes,
    INullableBoxEmitter nullableBoxes,
    IUnboxEmitter unboxes) :
    InstructionCommandProvider
{
    public override ImmutableArray<InstructionCommand> Commands =>
    [
        Command(CilOperation.Box, EmitBox),
        Command(CilOperation.Unbox, (request, code) => unboxes.Unbox(request, code, false)),
    ];

    private static InstructionCommand Command(
        CilOperation operation,
        Action<InstructionEmissionRequest, IWasmInstructionWriter> emit) => new(
        operation,
        InstructionFamily.AllocationBoxingTypes,
        emit);

    private void EmitBox(InstructionEmissionRequest request, IWasmInstructionWriter code)
    {
        roots.Emit(request, code);
        var slot = request.Stack.Count - 1;
        var type = types.Resolve(request.Instruction, request.Header.MethodInstance);
        if (!type.IsValueType)
        {
            if (request.Stack[slot] != CliValueKind.ManagedReference)
            {
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.UnsupportedCil,
                    $"box of reference type '{type.CanonicalName}' requires a managed reference"));
            }
            return;
        }
        var nullableUnderlyingType = nullableTypes.Resolve(type);
        if (nullableUnderlyingType is not null)
        {
            nullableBoxes.Emit(request, code, nullableUnderlyingType);
            return;
        }
        var value = values.GetValueLayout(type);
        var boxed = typeLayouts.GetObjectLayout(type);
        var payloadOffset = WasmTargetLayout.Align(
            layouts.Target.ObjectHeaderSize,
            value.Alignment);
        addresses.Emit(code, boxed.Size);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(boxed.TypeId)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(runtimeImports.Resolve(RuntimeImportSymbol.Allocate)))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        EmitAllocationFailureCheck(code, request.Context.ObjectTemporary);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        addresses.Emit(code, payloadOffset);
        addresses.Emit(code, AddressOperation.Add);
        if (type.StackKind == CliValueKind.ValueType)
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                slot,
                CliValueKind.ValueType)))));
            addresses.Emit(code, value.Size);
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.Prefixed, WasmInstructionOperand.PrefixedTriple(WasmOpcodes.MemoryCopy, 0, 0)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
                request.Context,
                slot,
                type.StackKind)))));
            ManagedMemoryEmitter.EmitStoreByType(
                code,
                layouts.Target,
                0,
                type,
                value.Size);
        }
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Context.ObjectTemporary))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(GetStackLocal(
            request.Context,
            slot,
            CliValueKind.ManagedReference)))));
        request.Stack[slot] = CliValueKind.ManagedReference;
    }

    private void EmitAllocationFailureCheck(IWasmInstructionWriter code, int objectLocal)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.OutOfMemory);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
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
