using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IEntryPointValidator
{
    void Validate(MethodDefinitionModel entryPoint);
}
