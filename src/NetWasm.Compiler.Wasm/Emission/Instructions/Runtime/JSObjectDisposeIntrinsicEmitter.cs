using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Support;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class JSObjectDisposeIntrinsicEmitter(
    ITargetLayout layouts,
    IImplicitExceptionEmitter exceptions,
    IAddressInstructionEmitter addresses) : IRuntimeIntrinsicEmitter
{
    public void Emit(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code)
    {
        var release = request.Instruction.Target.InteropImports.ReleaseHandle;
        if (!release.IsPresent)
        {
            // Without the host's release import no JavaScript import ran to give
            // an object a handle, so there is none to release: a program that
            // reaches Dispose without a JavaScript host keeps only its null check.
            EmitNullCheck(code, request.Local(0, CliValueKind.ManagedReference),
                request.Instruction.Context.ManagedThrowFunction);
            return;
        }
        EmitDispose(request, code, release.Value);
    }

    private void EmitDispose(RuntimeIntrinsicEmissionRequest request, IWasmInstructionWriter code, int release)
    {
        var objectLocal = request.Local(0, CliValueKind.ManagedReference);
        EmitNullCheck(code, objectLocal, request.Instruction.Context.ManagedThrowFunction);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, layouts.Target.ObjectHeaderSize);
        addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Load, WasmInstructionOperand.Memory(2, (uint)(0))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalSet, WasmInstructionOperand.Unsigned((uint)(request.Instruction.Context.InteropHandle))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Instruction.Context.InteropHandle))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32EqualZero));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Else));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(request.Instruction.Context.InteropHandle))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.Call, WasmInstructionOperand.Unsigned((uint)(release))));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, layouts.Target.ObjectHeaderSize);
        addresses.Emit(code, AddressOperation.Add);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Constant, WasmInstructionOperand.Signed(0)));
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.I32Store, WasmInstructionOperand.Memory(2, (uint)(0))));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }

    private void EmitNullCheck(
        IWasmInstructionWriter code,
        int objectLocal,
        OptionalFunctionIndex throwFunction)
    {
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.LocalGet, WasmInstructionOperand.Unsigned((uint)(objectLocal))));
        addresses.Emit(code, AddressOperation.EqualZero);
        code.Write(WasmInstruction.WithOperand(WasmOpcodes.If, WasmInstructionOperand.BlockType(WasmOpcodes.EmptyBlockType)));
        exceptions.Emit(code, ManagedExceptionKind.NullReference, throwFunction);
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.End));
    }
}
