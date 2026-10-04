using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface ISynchronousJSExportFunctionTypeResolver
{
    WasmFunctionType Resolve(MethodDefinitionModel method);
}
