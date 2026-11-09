using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IComponentBuiltInImportResolver
{
    WasmFunctionImport Resolve(MethodDefinitionModel method);
}
