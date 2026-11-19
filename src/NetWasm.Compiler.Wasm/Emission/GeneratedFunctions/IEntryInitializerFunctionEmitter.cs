// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IEntryInitializerFunctionEmitter
{
    /// <summary>Emits the body every entry calls: the state initializer, then each module initializer.</summary>
    byte[] Emit(int stateInitializerIndex, ImmutableArray<ModuleInitializerCall> moduleInitializers);
}
