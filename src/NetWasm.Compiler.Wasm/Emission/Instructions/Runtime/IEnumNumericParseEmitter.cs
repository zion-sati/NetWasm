using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

/// <summary>
/// Emits arbitrary decimal parsing for one enum underlying integral type.
/// </summary>
internal interface IEnumNumericParseEmitter
{
    void Emit(EnumNumericParseEmissionRequest request, IWasmInstructionWriter code);
}
