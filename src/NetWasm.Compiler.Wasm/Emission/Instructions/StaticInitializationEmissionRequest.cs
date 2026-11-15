// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

internal sealed record StaticInitializationEmissionRequest(
    EntityKey TypeDefinition,
    CliTypeIdentity? DeclaringType,
    IEntryLookup<string, StaticInitializerGuard> Guards,
    bool IsMethodInvocation = false);
