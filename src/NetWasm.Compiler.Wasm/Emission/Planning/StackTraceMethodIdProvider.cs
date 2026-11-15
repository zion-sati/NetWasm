// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
