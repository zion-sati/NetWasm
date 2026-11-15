// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IManagedThrowFunctionAppender
{
    // methods: the emitted managed methods, among them the reachable CoreLib
    // throwers managed.throw calls for an implicit exception's tag.
    void Append(
        IList<WasmFunctionDefinition> functions,
        OptionalFunctionIndex throwFunctionIndex,
        OptionalFunctionIndex rethrowFunctionIndex,
        ImmutableArray<ManagedDefinitionEmission> methods,
        IFunctionIndexResolver functionIndices);
}
