using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed class ImplicitExceptionEmitter : IImplicitExceptionEmitter
{
    private readonly ITargetLayout _layouts;
    private readonly IManagedExceptionObjectProvider _exceptions;
    private readonly int _beginThrowImportIndex;

    public ImplicitExceptionEmitter(
        ITargetLayout layouts,
        IManagedExceptionObjectProvider exceptions,
        IRuntimeImportResolver runtimeImports) :
        this(layouts, exceptions, runtimeImports.Resolve(RuntimeImportSymbol.BeginThrow))
    {
    }

    internal ImplicitExceptionEmitter(
        ITargetLayout layouts,
        IManagedExceptionObjectProvider exceptions,
        int beginThrowImportIndex)
    {
        _layouts = layouts;
        _exceptions = exceptions;
        _beginThrowImportIndex = beginThrowImportIndex;
    }

    public void Emit(IWasmInstructionWriter code, ManagedExceptionKind kind)
    {
        var exception = _exceptions.GetExceptionObject(kind);
        EmitReferenceConstant(code, exception);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Call,
            WasmInstructionOperand.Unsigned((uint)_beginThrowImportIndex)));
        EmitReferenceConstant(code, exception);
        code.Write(WasmInstruction.WithOperand(
            WasmOpcodes.Throw,
            WasmInstructionOperand.Unsigned(0)));
    }

    private void EmitReferenceConstant(IWasmInstructionWriter code, int value)
    {
        if (_layouts.Target.UsesMemory64)
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I64Constant,
                WasmInstructionOperand.Signed64(value)));
        }
        else
        {
            code.Write(WasmInstruction.WithOperand(
                WasmOpcodes.I32Constant,
                WasmInstructionOperand.Signed(value)));
        }
    }
}
