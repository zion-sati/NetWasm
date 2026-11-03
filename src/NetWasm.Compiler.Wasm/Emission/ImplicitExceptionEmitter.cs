using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

// A failed check passes managed.throw its exception kind's tag, and managed.throw
// throws the exception CoreLib's getter for the kind returns.
internal sealed class ImplicitExceptionEmitter(ITargetLayout layouts) : IImplicitExceptionEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        ManagedExceptionKind kind,
        OptionalFunctionIndex throwFunctionIndex)
    {
        ArgumentNullException.ThrowIfNull(code);
        var functionIndex = throwFunctionIndex.Value;
        var exception = RuntimeExceptionGetters.Tag(kind);
        if (layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(exception)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(exception)));
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)functionIndex)));
        code.Write(WasmInstruction.NoOperand(WasmOpcodes.Unreachable));
    }
}
