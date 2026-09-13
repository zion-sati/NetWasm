using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal interface IBoxedValueTypeValidator
{
    void Validate(
        IWasmInstructionWriter code,
        int objectLocal,
        CliTypeIdentity targetType);
}
