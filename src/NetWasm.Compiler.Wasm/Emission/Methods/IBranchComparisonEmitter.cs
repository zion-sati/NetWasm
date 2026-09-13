using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal interface IBranchComparisonEmitter
{
    void Compare(
        IWasmInstructionWriter code,
        CilOperation operation,
        CliValueKind leftType,
        CliValueKind rightType);
}
