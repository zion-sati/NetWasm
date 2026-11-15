// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Metadata.Tests;

internal static class ManagedAssemblyTestFactory
{
    internal static ManagedAssembly Load(string path) =>
        new ManagedAssemblyLoader(
            new ManagedAssemblyImageReader(),
            new ValueTypeDefinitionStackKindResolver(),
            new NativeImportDeclarationReader(),
            new NativeCallbackDeclarationReader()).Load(path);
}
