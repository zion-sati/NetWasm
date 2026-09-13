using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.NativeIntegers;

internal sealed class NativeIntegerConversionEmitter(
    ITargetLayout layouts) : INativeIntegerConversionEmitter
{
    private readonly ITargetLayout _layouts = layouts ??
        throw new ArgumentNullException(nameof(layouts));

    public void Emit(IWasmInstructionWriter code, CliValueKind source, bool unsigned)
    {
        if (source == CliValueKind.ManagedAddress)
        {
            return;
        }
        if (_layouts.Target.UsesMemory64 && source == CliValueKind.I4)
        {
            code.Write(WasmInstruction.NoOperand(unsigned
                ? WasmOpcodes.I64ExtendI32Unsigned
                : WasmOpcodes.I64ExtendI32Signed));
        }
        else if (!_layouts.Target.UsesMemory64 && source == CliValueKind.I8)
        {
            code.Write(WasmInstruction.NoOperand(WasmOpcodes.I32WrapI64));
        }
        else if (source is not (
                     CliValueKind.I4 or CliValueKind.I8 or CliValueKind.NativeInt))
        {
            throw new InvalidOperationException(
                "unsupported conversion to native integer");
        }
    }
}
