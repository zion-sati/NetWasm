using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal sealed class StackTraceMethodIdProvider : IStackTraceMethodIdProvider
{
    public int GetId(
        StackTraceMethodLookups lookups,
        MethodDefinitionModel method,
        MethodInstanceModel? methodInstance)
    {
        ArgumentNullException.ThrowIfNull(lookups);
        ArgumentNullException.ThrowIfNull(method);

        if (methodInstance is { IsConstructed: true })
        {
            return lookups.ConstructedMethodIds.TryGetValue(
                methodInstance.CanonicalName,
                out var constructedId)
                ? constructedId
                : 0;
        }
        return lookups.DirectMethodIds.TryGetValue(method.Key, out var directId)
            ? directId
            : 0;
    }
}
