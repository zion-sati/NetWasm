// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ValueTypeRuntimeIntrinsicRegistryTests
{
    [Theory]
    [InlineData("Equals", RuntimeIntrinsic.ValueTypeEquals)]
    [InlineData("GetHashCode", RuntimeIntrinsic.ValueTypeGetHashCode)]
    public void MapsValueTypeRuntimeContract(
        string methodName,
        RuntimeIntrinsic expected)
    {
        var assembly = new AssemblyIdentity("CoreLib");
        var typeKey = new EntityKey(assembly, 1);
        var methodKey = new EntityKey(assembly, 2);
        var method = new MethodDefinitionModel(
            methodKey,
            typeKey,
            methodName,
            false,
            MethodSignatureModel.Create(CliValueKind.I4),
            0);
        var registry = new RuntimeIntrinsicRegistry(
            new ValueTypeSymbolFormatter(typeKey),
            [method]);

        Assert.True(registry.TryGetIntrinsic(methodKey, out var intrinsic));
        Assert.Equal(expected, intrinsic);
    }

    private sealed class ValueTypeSymbolFormatter(EntityKey typeKey) : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == typeKey
            ? "System.ValueType"
            : throw new KeyNotFoundException();

        public string Format(MethodDefinitionModel method) =>
            $"{Format(method.DeclaringType)}::{method.Name}";
    }
}
