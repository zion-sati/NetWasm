// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ReachabilityMarkersTests
{
    private static readonly AssemblyIdentity Assembly = new("NetWasm.CoreLib");

    [Theory]
    [InlineData("System", "Type", "get_Name", false)]
    [InlineData("System", "Type", "GetDelegateInvokeMethod", false)]
    [InlineData("System", "Type", "GetGenericArguments", true)]
    [InlineData("System.Reflection", "MemberInfo", "get_Name", true)]
    [InlineData("System", "Type", "get_IsValueType", false)]
    [InlineData("System.Reflection", "MemberInfo", "get_DeclaringType", false)]
    [InlineData("System", "Object", "GetGenericArguments", false)]
    [InlineData("System", "Type", "MakeArrayType", true)]
    [InlineData("System", "Type", "MakeByRefType", false)]
    public void TheInlinerKeepsEveryMarkerCall(string ns, string type, string method, bool marker)
    {
        var instance = Method(ns, type, method);

        Assert.Equal(marker, ReachabilityMarkers.IsMarker(instance));
        Assert.Equal(method == "GetGenericArguments" && type == "Type", ReachabilityMarkers.RequiresGenericArguments(instance));
        Assert.Equal(method == "get_Name" && type == "MemberInfo", ReachabilityMarkers.RequiresMemberNames(instance));
        Assert.Equal(method == "MakeArrayType" && type == "Type", ReachabilityMarkers.MakesArrayTypes(instance));
    }

    private static MethodInstanceModel Method(string ns, string type, string name)
    {
        var declaringType = CliTypeIdentity.Named(Assembly, ns, type, isValueType: false);
        var signature = new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), []);
        return new(
            new MethodDefinitionModel(new EntityKey(Assembly, 0x06000001), new EntityKey(Assembly, 0x02000001), name, false, signature, 1),
            declaringType,
            [],
            signature);
    }
}
