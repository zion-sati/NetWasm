// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class RuntimeInitializerFunctionAppender(
    IRuntimeInitializerFunctionEmitter initializer,
    IEntryInitializerFunctionEmitter entries) : IRuntimeInitializerFunctionAppender
{
    public RuntimeInitializationPlan Append(IList<WasmFunctionDefinition> functions, int importCount, RuntimeInitializationPlan plan)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentOutOfRangeException.ThrowIfNegative(importCount);
        ArgumentNullException.ThrowIfNull(plan);
        // Initialization runs once per entry and is cold; inlining would copy it into every entry.
        var state = Add("runtime.initialize", initializer.Emit(plan));
        // Without module initializers an entry needs only the state, so one function serves both.
        var entry = plan.ModuleInitializers.IsEmpty
            ? state
            : Add("runtime.initialize-entry", entries.Emit(state, plan.ModuleInitializers));
        return plan with
        {
            InitializerFunctionIndex = entry,
            StateInitializerFunctionIndex = state,
        };

        int Add(string name, byte[] body)
        {
            var index = importCount + functions.Count;
            functions.Add(new(name, WasmFunctionType.Create(CliValueKind.Void), body) { IsNoInlining = true });
            return index;
        }
    }
}
