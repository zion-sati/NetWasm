using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Tests;

using static EmitterTestSupport;

public sealed class FunctionIndexMapTests
{
    [Fact]
    public void ResolvesEachFunctionNamespaceWithoutAliasingThem()
    {
        var imported = Key(0x06000020);
        var map = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                EntryKey, new(30)),
            ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                "generic", new(31)),
            ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                "delegate", new(32)),
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(
                imported, new(33)));

        Assert.True(map.TryGetMethod(EntryKey, out var direct));
        Assert.True(map.TryGetMethod(imported, out var jsImport));
        Assert.True(map.TryGetConstructedMethod("generic", out var constructed));
        Assert.Equal(30, direct.Value);
        Assert.Equal(31, constructed.Value);
        Assert.Equal(32, map.GetDelegateInvokeHelper("delegate").Value);
        Assert.Equal(33, jsImport.Value);
        Assert.False(map.TryGetMethod(ConstructorKey, out _));
        Assert.False(map.TryGetConstructedMethod("missing", out _));
    }

    [Fact]
    public void MissingDelegateHelperIsRejected()
    {
        var map = new FunctionIndexMap(
            [],
            [],
            [],
            []);

        Assert.Throws<KeyNotFoundException>(() => map.GetDelegateInvokeHelper("missing"));
    }

    [Fact]
    public void OptionalFunctionIndexCannotMasqueradeAsARealIndex()
    {
        var present = OptionalFunctionIndex.At(0);
        var missing = OptionalFunctionIndex.Missing;

        Assert.True(present.IsPresent);
        Assert.Equal(0, present.Value);
        Assert.False(missing.IsPresent);
        Assert.Throws<InvalidOperationException>(() => missing.Value);
    }
}
