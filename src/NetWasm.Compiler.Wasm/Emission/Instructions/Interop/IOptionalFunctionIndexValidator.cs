using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal interface IOptionalFunctionIndexValidator
{
    void Validate(OptionalFunctionIndex index, string message);
}
