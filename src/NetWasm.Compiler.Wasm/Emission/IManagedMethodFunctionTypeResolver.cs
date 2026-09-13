using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal interface IManagedMethodFunctionTypeResolver
{
    WasmFunctionType Resolve(MethodDefinitionModel method);

    WasmFunctionType Resolve(MethodInstanceModel method);
}
