// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class GCHandleRuntimeIntrinsicRegistryTests
{
    [Theory]
    [InlineData("System.GCHandleRuntime", "Create", RuntimeIntrinsic.GcHandleCreate)]
    [InlineData("System.GCHandleRuntime", "Get", RuntimeIntrinsic.GcHandleGet)]
    [InlineData("System.GCHandleRuntime", "Address", RuntimeIntrinsic.GcHandleAddress)]
    [InlineData("System.GCHandleRuntime", "Set", RuntimeIntrinsic.GcHandleSet)]
    [InlineData("System.GCHandleRuntime", "Release", RuntimeIntrinsic.GcHandleRelease)]
    [InlineData("System.GCMetricRuntime", "Read", RuntimeIntrinsic.GcGetMetric)]
    [InlineData("System.GCCollectionRuntime", "Collect", RuntimeIntrinsic.GcCollect)]
    [InlineData("System.ObjectIdentityRuntime", "GetHashCode", RuntimeIntrinsic.ObjectIdentityHash)]
    [InlineData("System.MarvinSeedRuntime", "Get", RuntimeIntrinsic.RandomSeed)]
    [InlineData("System.Buffer", "RecordReferenceWrites", RuntimeIntrinsic.RecordReferenceWrites)]
    [InlineData("System.GCGenerationRuntime", "Of", RuntimeIntrinsic.GcObjectGeneration)]
    [InlineData("System.GCCollectionRuntime", "CollectYoung", RuntimeIntrinsic.GcCollectYoung)]
    [InlineData("System.GCCollectionRuntime", "CollectWhenIdle", RuntimeIntrinsic.GcCollectWhenIdle)]
    [InlineData("System.GCCollectionRuntime", "CollectWhenQuiet", RuntimeIntrinsic.GcCollectWhenQuiet)]
    [InlineData(
        "System.GCFinalizerRuntime",
        "WaitForPending",
        RuntimeIntrinsic.GcWaitForPendingFinalizers)]
    [InlineData("System.GCFinalizerRuntime", "RunQueued", RuntimeIntrinsic.GcRunQueuedFinalizers)]
    public void MapsEveryGcRuntimeContract(
        string typeName,
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
            true,
            new MethodSignatureModel(CliValueKind.Void, ImmutableArray<CliValueKind>.Empty),
            0);
        var registry = new RuntimeIntrinsicRegistry(
            new GCHandleRuntimeSymbolFormatter(typeKey, typeName),
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

    private sealed class GCHandleRuntimeSymbolFormatter(EntityKey typeKey, string typeName) : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == typeKey
                ? typeName
            : throw new KeyNotFoundException();

        public string Format(MethodDefinitionModel method) => $"{Format(method.DeclaringType)}::{method.Name}";
    }
}
