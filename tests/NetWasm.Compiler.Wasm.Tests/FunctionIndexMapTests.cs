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
                imported, new(33)))
        {
            DelegateMulticastHelpers = ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add(
                "delegate", new(34)),
        };

        Assert.True(map.TryGetMethod(EntryKey, out var direct));
        Assert.True(map.TryGetMethod(imported, out var jsImport));
        Assert.True(map.TryGetConstructedMethod("generic", out var constructed));
        Assert.Equal(30, direct.Value);
        Assert.Equal(31, constructed.Value);
        Assert.Equal(32, map.GetDelegateInvokeHelper("delegate").Value);
        Assert.Equal(34, map.GetDelegateMulticastHelper("delegate").Value);
        Assert.Equal(33, jsImport.Value);
        Assert.False(map.TryGetMethod(ConstructorKey, out _));
        Assert.False(map.TryGetConstructedMethod("missing", out _));
    }

    // A delegate converted from another delegate binds its Invoke, whose address is the
    // Invoke's type's invoke helper (issue 223): a definition by key, a generic delegate's
    // by canonical name.
    [Fact]
    public void ADelegatesInvokeResolvesToItsTypesInvokeHelper()
    {
        var invoke = Key(0x06000030);
        var map = new FunctionIndexMap(
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(EntryKey, new(30)),
            ImmutableDictionary<string, WasmFunctionIndex>.Empty.Add("generic", new(31)),
            ImmutableDictionary<string, WasmFunctionIndex>.Empty
                .Add("Transform", new(40))
                .Add("Func`2<int,int>", new(41)),
            [])
        {
            DelegateInvokeMethods = ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(invoke, new(40)),
            ConstructedDelegateInvokeMethods = ImmutableDictionary<string, WasmFunctionIndex>.Empty
                .Add("Func`2<int,int>::Invoke", new(41)),
        };

        Assert.True(map.TryGetMethod(invoke, out var definition));
        Assert.True(map.TryGetConstructedMethod("Func`2<int,int>::Invoke", out var constructed));
        Assert.Equal(40, definition.Value);
        Assert.Equal(41, constructed.Value);
        Assert.True(map.TryGetConstructedMethod("generic", out var method));
        Assert.Equal(31, method.Value);
        Assert.False(map.TryGetConstructedMethod("Func`2<int,int>", out _));
    }

    [Fact]
    public void JSImportForwarderShadowsTheRawImportOnlyForManagedResolution()
    {
        var imported = Key(0x06000020);
        var map = new FunctionIndexMap(
            [],
            [],
            [],
            ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty.Add(imported, new(33)))
        {
            JSImportForwarders = ImmutableDictionary<EntityKey, WasmFunctionIndex>.Empty
                .Add(imported, new(44)),
        };

        Assert.True(map.TryGetMethod(imported, out var managed));
        Assert.True(map.TryGetInteropImport(imported, out var raw));
        Assert.Equal(44, managed.Value);
        Assert.Equal(33, raw.Value);
        Assert.False(map.TryGetInteropImport(EntryKey, out _));
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
        Assert.Throws<KeyNotFoundException>(() => map.GetDelegateMulticastHelper("missing"));
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
