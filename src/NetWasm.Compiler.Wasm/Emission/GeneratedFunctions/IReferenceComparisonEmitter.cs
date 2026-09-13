using NetWasm.Compiler.Wasm.Encoding;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IReferenceComparisonEmitter
{
    void Emit(
        IWasmInstructionWriter code,
        ReferenceComparison comparison,
        CliValueKind operandType = CliValueKind.ManagedReference);
}
