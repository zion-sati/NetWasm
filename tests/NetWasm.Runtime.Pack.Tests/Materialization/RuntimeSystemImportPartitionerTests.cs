// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeSystemImportPartitionerTests
{
    [Fact]
    public void SeparatesCLibraryImportsAsRuntimeExportsFromNativeLibraryImports()
    {
        var capability = Assert.IsAssignableFrom<IRuntimeSystemImportPartitioner>(new RuntimeSystemImportPartitioner());
        var native = Import("libsample", "sample_value");
        var differentCase = Import("LIBC", "cbrt");

        var partition = capability.Partition(
            [Import("libc", "log10"), native, Import("libc", "cbrt"), differentCase, Import("libc", "log10")]);

        Assert.Equal([native, differentCase], partition.NativeImports.ToArray());
        Assert.Equal(["cbrt", "log10"], partition.SystemExports.ToArray());
    }

    [Fact]
    public void AProgramWithoutCLibraryImportsKeepsItsNativeImports()
    {
        var native = Import("libsample", "sample_value");

        var partition = new RuntimeSystemImportPartitioner().Partition([native]);

        Assert.Equal([native], partition.NativeImports.ToArray());
        Assert.Empty(partition.SystemExports);
    }

    [Fact]
    public void RejectsMissingImports()
    {
        var partitioner = new RuntimeSystemImportPartitioner();

        Assert.Throws<InvalidOperationException>(() => partitioner.Partition(default));
        Assert.Throws<InvalidOperationException>(() => partitioner.Partition([null!]));
    }

    private static RuntimeNativeImport Import(string library, string entryPoint) =>
        new(library, entryPoint, [RuntimeNativeValueType.F64], RuntimeNativeValueType.F64);
}
