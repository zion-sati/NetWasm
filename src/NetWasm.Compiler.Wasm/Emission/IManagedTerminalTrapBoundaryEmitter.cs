using System;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IManagedTerminalTrapBoundaryEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        CliValueKind resultType,
        int resultLocal,
        Action emitBody);
}
