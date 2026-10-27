using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSExportResultEmitter(
    ITargetLayout layouts,
    IInstanceFieldLayoutProvider fieldLayouts,
    IRuntimeImportResolver runtimeImports,
    IAddressInstructionEmitter addresses,
    IJavaScriptNullableEmitter nullables,
    IGeneratedFunctionWriterFactory writers) : IAsyncJSExportResultEmitter
{
    public byte[] Emit(JavaScriptAsyncMethodBinding binding, InteropImportPlan? interopImports = null)
    {
        var resultType = binding.Return.ResultType ?? throw new InvalidOperationException(
            "a result reader requires Task<T> or ValueTask<T>");
        var field = fieldLayouts.GetFieldLayout(binding.ResultField!);
        var hostObject = InteropTypeClassifier.IsJSObject(resultType);
        var nullable = InteropTypeClassifier.IsNullable(resultType);
        var code = writers.Create();
        WasmLocalDeclarationWriter.Write(
            code.Bytes,
            hostObject ? [CliValueKind.ManagedReference]
                : nullable ? [CliValueKind.ManagedAddress, CliValueKind.ManagedAddress]
                : [],
            layouts.Target);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(0)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(
                RuntimeImportSymbol.HandleGet))));
        if (nullable)
        {
            // The task holds the nullable's struct. Its record is written to a value
            // frame and its address returned; the host reads it before calling in again.
            var interop = interopImports ?? throw new InvalidOperationException(
                "a nullable result is read only at a JavaScript boundary");
            addresses.Emit(code.Instructions, field.Offset);
            addresses.Emit(code.Instructions, AddressOperation.Add);
            Local(code, WasmOpcodes.LocalSet, 1);
            ManagedMemoryEmitter.EmitAddressConstant(code.Instructions, layouts.Target, JavaScriptNullable.RecordSize);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameEnter))));
            Local(code, WasmOpcodes.LocalSet, 2);
            nullables.Emit(
                code.Instructions,
                new JavaScriptNullableToRecord(resultType, Struct: new(1), Record: new(2)),
                interop);
            Local(code, WasmOpcodes.LocalGet, 2);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)runtimeImports.Resolve(RuntimeImportSymbol.ValueFrameLeave))));
            Local(code, WasmOpcodes.LocalGet, 2);
            code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            return code.Snapshots.Read();
        }
        if (InteropTypeClassifier.IsDate(resultType))
        {
            // The task holds the date's struct; JavaScript reads its Unix milliseconds.
            addresses.Emit(code.Instructions, field.Offset);
            addresses.Emit(code.Instructions, AddressOperation.Add);
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)(interopImports ?? throw new InvalidOperationException(
                    "a date result is read only at a JavaScript boundary")).FromManaged(resultType))));
            code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
            return code.Snapshots.Read();
        }
        ManagedMemoryEmitter.EmitLoadByType(
            code.Instructions,
            layouts.Target,
            field.Offset,
            resultType,
            field.Size);
        if (InteropTypeClassifier.IsHandleValue(resultType))
        {
            // The host reads the converted value through the handle and releases it.
            code.Instructions.Write(WasmInstruction.WithOperand(
                WasmOpcodes.Call,
                WasmInstructionOperand.Unsigned((uint)(interopImports ?? throw new InvalidOperationException(
                    "a converted result is read only at a JavaScript boundary")).FromManaged(resultType))));
        }
        if (hostObject)
        {
            EmitHostObjectHandle(code);
        }
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
        return code.Snapshots.Read();
    }

    private static void Local(GeneratedFunctionWriterLease code, byte opcode, int local) =>
        code.Instructions.Write(WasmInstruction.WithOperand(opcode, WasmInstructionOperand.Unsigned((uint)local)));

    // The JSObject on the stack becomes its host handle, or 0 for null, as a
    // synchronous export's result does. Local 1 holds the object.
    private void EmitHostObjectHandle(GeneratedFunctionWriterLease code)
    {
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalTee,
            WasmInstructionOperand.Unsigned(1)));
        addresses.Emit(code.Instructions, AddressOperation.EqualZero);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.If,
            WasmInstructionOperand.BlockType((byte)WasmValueType.I32)));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalGet,
            WasmInstructionOperand.Unsigned(1)));
        addresses.Emit(code.Instructions, layouts.Target.ObjectHeaderSize);
        addresses.Emit(code.Instructions, AddressOperation.Add);
        code.Instructions.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Load,
            WasmInstructionOperand.Memory(2, 0)));
        code.Instructions.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
