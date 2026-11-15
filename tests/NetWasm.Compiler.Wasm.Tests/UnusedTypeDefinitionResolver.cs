// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Tests;

// For emitters whose type-definition lookups are disabled in a test.
internal sealed class UnusedTypeDefinitionResolver : ITypeDefinitionResolver
{
    public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
        throw new InvalidOperationException("This test does not resolve type definitions.");
}
