// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Wasm.Tests;

internal sealed class TestCilTypeIdentityResolver : ICilTypeIdentityResolver
{
    private static readonly CliTypeIdentity TestType = CliTypeIdentity.Named(
        new AssemblyIdentity("Tests"),
        "Tests",
        "Container",
        isValueType: false);

    public CliTypeIdentity Resolve(EntityKey key) => TestType;
}
