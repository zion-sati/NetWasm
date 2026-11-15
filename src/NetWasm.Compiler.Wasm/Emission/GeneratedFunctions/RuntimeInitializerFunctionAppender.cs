// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class RuntimeInitializerFunctionAppender(
    IRuntimeInitializerFunctionEmitter initializer) : IRuntimeInitializerFunctionAppender
{
    public int Append(IList<WasmFunctionDefinition> functions, int importCount, RuntimeInitializationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(plan);
        var index = importCount + functions.Count;
        // Initialization runs once per entry and is cold; inlining would copy it into every entry.
        functions.Add(new("runtime.initialize", WasmFunctionType.Create(CliValueKind.Void), initializer.Emit(plan))
        {
            IsNoInlining = true,
        });
        return index;
    }
}
