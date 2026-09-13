using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class StackTraceMethodIdProvider : IStackTraceMethodIdProvider
{
    public int GetId(
        StackTraceMethodPlan plan,
        MethodDefinitionModel method,
        MethodInstanceModel? methodInstance)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(method);

        if (methodInstance is { IsConstructed: true })
        {
            return plan.ConstructedMethodIds.TryGetValue(
                methodInstance.CanonicalName,
                out var constructedId)
                ? constructedId
                : 0;
        }
        return plan.DirectMethodIds.TryGetValue(method.Key, out var directId)
            ? directId
            : 0;
    }
}
