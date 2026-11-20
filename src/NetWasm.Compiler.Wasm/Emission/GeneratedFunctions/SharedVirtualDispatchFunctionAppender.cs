// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Calls;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface ISharedVirtualDispatchFunctionAppender
{
    void Append(
        IList<WasmFunctionDefinition> functions,
        ImmutableArray<SharedVirtualDispatchPlan> dispatches,
        IEntryLookup<string, StaticInitializerGuard> staticInitializerGuards,
        IFunctionIndexResolver functionIndices);
}

// Each shared virtual dispatch function, in the order the module plan numbers them, after the delegate helpers.
internal sealed class SharedVirtualDispatchFunctionAppender(
    ISharedVirtualDispatchFunctionEmitter emitter,
    IManagedMethodFunctionTypeResolver functionTypes) : ISharedVirtualDispatchFunctionAppender
{
    public void Append(
        IList<WasmFunctionDefinition> functions,
        ImmutableArray<SharedVirtualDispatchPlan> dispatches,
        IEntryLookup<string, StaticInitializerGuard> staticInitializerGuards,
        IFunctionIndexResolver functionIndices)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(staticInitializerGuards);
        ArgumentNullException.ThrowIfNull(functionIndices);
        foreach (var dispatch in dispatches)
        {
            functions.Add(new(
                $"virtual.dispatch<{dispatch.Declaration.CanonicalName}>",
                functionTypes.Resolve(dispatch.Declaration),
                emitter.Emit(dispatch, staticInitializerGuards, functionIndices)));
        }
    }
}
