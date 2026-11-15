// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class WeakReferenceRuntimeIntrinsicRegistryTests
{
    [Theory]
    [InlineData("Create", RuntimeIntrinsic.WeakHandleCreate)]
    [InlineData("Get", RuntimeIntrinsic.WeakHandleGet)]
    [InlineData("Set", RuntimeIntrinsic.WeakHandleSet)]
    [InlineData("Release", RuntimeIntrinsic.WeakHandleRelease)]
    public void MapsEveryWeakHandleRuntimeContract(string methodName, RuntimeIntrinsic expected)
    {
        var assembly = new AssemblyIdentity("CoreLib");
        var typeKey = new EntityKey(assembly, 1);
        var methodKey = new EntityKey(assembly, 2);
        var method = new MethodDefinitionModel(
            methodKey,
            typeKey,
            methodName,
            true,
            new MethodSignatureModel(CliValueKind.Void, ImmutableArray<CliValueKind>.Empty),
            0);
        var registry = new RuntimeIntrinsicRegistry(
            new WeakReferenceRuntimeSymbolFormatter(typeKey),
            [method]);

        AssertMapping(registry, methodKey, expected);
    }

    private static void AssertMapping<TRegistry>(
        TRegistry registry,
        EntityKey methodKey,
        RuntimeIntrinsic expected)
        where TRegistry : IRuntimeIntrinsicRegistry
    {
        Assert.True(registry.TryGetIntrinsic(methodKey, out var intrinsic));
        Assert.Equal(expected, intrinsic);
    }

    private sealed class WeakReferenceRuntimeSymbolFormatter(EntityKey typeKey) : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == typeKey
            ? "System.WeakReferenceRuntime"
            : throw new KeyNotFoundException();

        public string Format(MethodDefinitionModel method) => $"{Format(method.DeclaringType)}::{method.Name}";
    }
}
