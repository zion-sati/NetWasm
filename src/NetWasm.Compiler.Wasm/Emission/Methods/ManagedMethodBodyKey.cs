// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Methods;

internal static class ManagedMethodBodyKey
{
    public static string Resolve(StructuredMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return Resolve(method.Header);
    }

    public static string Resolve(StructuredMethodHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return header.MethodInstance?.CanonicalName ?? header.Method.Key.ToString();
    }

}
