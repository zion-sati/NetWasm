// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;

namespace NetWasm.Compiler.Core.NativeInterop;

// A compile that links no archives: no function is called as a variadic one.
public sealed class NoNativeVariadicCalls : INativeVariadicCallPlanner
{
    public static NoNativeVariadicCalls Instance { get; } = new();

    public int? FixedParameters(string libraryName, string entryPoint, IReadOnlyCollection<MethodSignatureModel> declarations)
    {
        ArgumentNullException.ThrowIfNull(libraryName);
        ArgumentNullException.ThrowIfNull(entryPoint);
        ArgumentNullException.ThrowIfNull(declarations);
        return null;
    }
}
