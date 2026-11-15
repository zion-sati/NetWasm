// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Core.Tests;

public sealed class NetWasmRuntimeFeatureIdsTests
{
    [Fact]
    public void AllListsEveryDeclaredFeatureInOrdinalOrder()
    {
        var declared = typeof(NetWasmRuntimeFeatureIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(declared);
        Assert.Equal(declared, NetWasmRuntimeFeatureIds.All);
    }
}
