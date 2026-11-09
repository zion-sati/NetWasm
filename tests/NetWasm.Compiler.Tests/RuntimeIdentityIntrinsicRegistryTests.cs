using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class RuntimeIdentityIntrinsicRegistryTests
{
    [Fact]
    public void MapsTheHostFlavourGetterToItsIntrinsic()
    {
        var assembly = new AssemblyIdentity("CoreLib");
        var typeKey = new EntityKey(assembly, 1);
        var methodKey = new EntityKey(assembly, 2);
        var method = new MethodDefinitionModel(
            methodKey,
            typeKey,
            "get_IsJavaScriptHost",
            true,
            new MethodSignatureModel(CliValueKind.I4, ImmutableArray<CliValueKind>.Empty),
            0);
        var registry = new RuntimeIntrinsicRegistry(
            new SingleTypeSymbolFormatter(typeKey, "System.Runtime.InteropServices.NetWasmHost"),
            [method]);

        Assert.True(registry.TryGetIntrinsic(methodKey, out var intrinsic));
        Assert.Equal(RuntimeIntrinsic.JavaScriptHost, intrinsic);
    }

    private sealed class SingleTypeSymbolFormatter(EntityKey typeKey, string typeName) : ISymbolFormatter
    {
        public string Format(EntityKey key) => key == typeKey ? typeName : throw new KeyNotFoundException();

        public string Format(MethodDefinitionModel method) => $"{Format(method.DeclaringType)}::{method.Name}";
    }
}
