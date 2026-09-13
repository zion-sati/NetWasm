using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IHostCallbackFunctionTypeResolver
{
    WasmFunctionType Resolve(MethodInstanceModel invoke);
}
