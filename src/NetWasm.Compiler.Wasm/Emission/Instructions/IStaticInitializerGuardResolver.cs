// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Diagnostics.CodeAnalysis;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.Instructions;

// The planned guard that runs an owner's static constructor before a member access, when the
// access needs one: none for a BeforeFieldInit owner's method call, or an owner without one.
internal interface IStaticInitializerGuardResolver
{
    bool TryResolve(StaticInitializationEmissionRequest request, [NotNullWhen(true)] out StaticInitializerGuard? guard);
}
