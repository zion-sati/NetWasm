using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IStackTraceMethodIdProvider
{
    int GetId(
        StackTraceMethodLookups lookups,
        MethodDefinitionModel method,
        MethodInstanceModel? methodInstance);
}
