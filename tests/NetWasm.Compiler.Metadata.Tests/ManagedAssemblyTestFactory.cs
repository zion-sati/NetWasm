// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Metadata.Tests;

internal static class ManagedAssemblyTestFactory
{
    internal static ManagedAssembly Load(string path, NetWasm.Compiler.Core.NativeInterop.INativeImportBinder? binder = null,
        NetWasm.Compiler.Core.NativeInterop.INativeVariadicCallPlanner? variadics = null,
        NetWasm.Compiler.Core.GeneratedJavaScriptBinding? javaScriptBinding = null) =>
        new ManagedAssemblyLoader(
            new ManagedAssemblyImageReader(),
            new ValueTypeDefinitionStackKindResolver(),
            new NativeImportDeclarationReader(),
            new NativeCallbackDeclarationReader()).Load(path, System.Collections.Immutable.ImmutableDictionary<string, string>.Empty, nativeBinding: binder, nativeVariadics: variadics,
                javaScriptBinding: javaScriptBinding);
}
