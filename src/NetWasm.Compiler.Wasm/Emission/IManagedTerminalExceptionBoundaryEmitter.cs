using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IManagedTerminalExceptionBoundaryEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        int exceptionLocal,
        int rootFrameLocal,
        int typeIdLocal,
        int messageLocal,
        int messageLengthLocal,
        CliValueKind resultType,
        int resultLocal,
        int reportFunctionIndex,
        Action emitBody);
}
