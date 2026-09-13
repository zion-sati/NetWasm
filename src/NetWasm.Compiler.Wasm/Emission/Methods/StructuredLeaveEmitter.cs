using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal sealed class StructuredLeaveEmitter : IStructuredLeaveEmitter
{
    public void Emit(
        IWasmInstructionWriter code,
        CilInstruction instruction,
        StructuredContinuationId? continuation,
        MethodEmissionContext context)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(instruction);
        ArgumentNullException.ThrowIfNull(context);

        var group = context.ActiveExceptionGroup
                    ?? throw new InvalidOperationException(
                        $"A leave instruction at IL_{instruction.Offset:x4} in " +
                        $"method '{context.RootMap.Method}' was emitted without an " +
                        "active exception group.");
        if (continuation is not { } target)
        {
            throw new InvalidOperationException(
                $"Leave instruction at IL_{instruction.Offset:x4} has no structured continuation.");
        }
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.I32Constant,
            WasmInstructionOperand.Signed(target.Value + 1)));
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.LocalSet,
            WasmInstructionOperand.Unsigned((uint)context.ExceptionContinuationLocals[group.Id])));
    }
}
