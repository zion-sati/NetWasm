// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class GCMetricRuntimeIntrinsicRegistryTests
{
    [Theory]
    [InlineData("System.GCMetricRuntime", "Read", RuntimeIntrinsic.GcGetMetric)]
    [InlineData(
        "System.GCMetricSupportRuntime",
        "IsSupported",
        RuntimeIntrinsic.GcMetricIsSupported)]
    public void RegistryMapsEachGcMetricRuntimeContract(
        string typeName,
        string methodName,
        RuntimeIntrinsic expected)
    {
        var assembly = new AssemblyIdentity("GCMetricIntrinsicTests");
        var declaringType = new EntityKey(assembly, 1);
        var method = new MethodDefinitionModel(
            new EntityKey(assembly, 2),
            declaringType,
            methodName,
            IsStatic: true,
            MethodSignatureModel.Create(
                expected == RuntimeIntrinsic.GcGetMetric ? CliValueKind.I8 : CliValueKind.I4,
                CliValueKind.I4),
            RelativeVirtualAddress: 0);
        var registry = new RuntimeIntrinsicRegistry(
            new FixedSymbolFormatter(declaringType, typeName),
            [method]);

        Assert.True(registry.TryGetIntrinsic(method.Key, out var actual));
        Assert.Equal(expected, actual);
    }

    private sealed class FixedSymbolFormatter(EntityKey declaringType, string typeName)
        : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == declaringType ? typeName : "Other.Type";

        public string Format(MethodDefinitionModel method) =>
            Format(method.DeclaringType) + "::" + method.Name;
    }
}
