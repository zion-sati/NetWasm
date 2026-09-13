using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IStackTraceMethodIdProvider
{
    int GetId(
        StackTraceMethodPlan plan,
        MethodDefinitionModel method,
        MethodInstanceModel? methodInstance);
}
