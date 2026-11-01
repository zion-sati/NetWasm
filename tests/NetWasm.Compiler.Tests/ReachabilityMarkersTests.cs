using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class ReachabilityMarkersTests
{
    private static readonly AssemblyIdentity Assembly = new("NetWasm.CoreLib");

    [Theory]
    [InlineData("System", "Type", "GetRuntimeName", RuntimeTypeNamePayload.Name)]
    [InlineData("System", "Type", "GetRuntimeNamespace", RuntimeTypeNamePayload.Namespace)]
    [InlineData("System", "Type", "GetRuntimeFullName", RuntimeTypeNamePayload.FullName)]
    [InlineData("System", "Type", "GetRuntimeDisplayName", RuntimeTypeNamePayload.DisplayName)]
    [InlineData("System", "Type", "get_Name", RuntimeTypeNamePayload.None)]
    [InlineData("System", "Object", "GetRuntimeName", RuntimeTypeNamePayload.None)]
    public void TypeNameReadersNameTheirPayload(string ns, string type, string method, RuntimeTypeNamePayload payload) =>
        Assert.Equal(payload, ReachabilityMarkers.TypeNamePayload(Method(ns, type, method)));

    [Theory]
    [InlineData("System", "Type", "GetRuntimeName", true)]
    [InlineData("System", "Type", "GetDelegateInvokeMethod", true)]
    [InlineData("System", "Type", "GetGenericArguments", true)]
    [InlineData("System.Reflection", "MemberInfo", "get_Name", true)]
    [InlineData("System", "Type", "get_IsValueType", false)]
    [InlineData("System.Reflection", "MemberInfo", "get_DeclaringType", false)]
    [InlineData("System", "Object", "GetGenericArguments", false)]
    public void TheInlinerKeepsEveryMarkerCall(string ns, string type, string method, bool marker)
    {
        var instance = Method(ns, type, method);

        Assert.Equal(marker, ReachabilityMarkers.IsMarker(instance));
        Assert.Equal(method == "GetDelegateInvokeMethod" && type == "Type", ReachabilityMarkers.RequiresDelegateInvoke(instance));
        Assert.Equal(method == "GetGenericArguments" && type == "Type", ReachabilityMarkers.RequiresGenericArguments(instance));
        Assert.Equal(method == "get_Name" && type == "MemberInfo", ReachabilityMarkers.RequiresMemberNames(instance));
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
