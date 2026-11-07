using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Units;

internal interface IWasmFunctionSymbolBuilder
{
    // Names every function of a planned module whose identity outlives this compile.
    WasmFunctionSymbols Build(
        WasmModulePlan plan,
        ModuleDataPlan data,
        IReadOnlyList<WasmFunctionImport> imports);
}

internal sealed class WasmFunctionSymbolBuilder : IWasmFunctionSymbolBuilder
{
    public WasmFunctionSymbols Build(
        WasmModulePlan plan,
        ModuleDataPlan data,
        IReadOnlyList<WasmFunctionImport> imports)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(imports);
        var byIndex = new Dictionary<uint, string>();
        var bySymbol = new Dictionary<string, uint>(StringComparer.Ordinal);
        var ambiguousIndices = new HashSet<uint>();
        var ambiguousSymbols = new HashSet<string>(StringComparer.Ordinal);
        void Add(uint index, string symbol)
        {
            if (!byIndex.TryAdd(index, symbol) && byIndex[index] != symbol)
                ambiguousIndices.Add(index);
            if (!bySymbol.TryAdd(symbol, index) && bySymbol[symbol] != index)
                ambiguousSymbols.Add(symbol);
        }

        for (var index = 0; index < imports.Count; index++)
            Add((uint)index, $"import:{imports[index].Module}\u0001{imports[index].Name}");

        var indices = plan.FunctionIndices;
        var canonicalByKey = new Dictionary<EntityKey, string>(plan.OrderedMethods.Length);
        foreach (var method in plan.OrderedMethods)
        {
            canonicalByKey[method.MethodKey] = method.Identity.CanonicalName;
            if (indices.DirectMethods.TryGetValue(method.MethodKey, out var index))
                Add((uint)index.Value, "method:" + method.Identity.CanonicalName);
        }
        foreach (var (name, index) in indices.ConstructedMethods)
            Add((uint)index.Value, "method:" + name);
        foreach (var (delegateType, index) in indices.DelegateInvokeHelpers)
            Add((uint)index.Value, "delegate-invoke:" + delegateType);
        foreach (var (delegateType, index) in indices.DelegateMulticastHelpers)
            Add((uint)index.Value, "delegate-multicast:" + delegateType);
        foreach (var forwarder in plan.JSImportForwarders)
        {
            if (indices.JSImportForwarders.TryGetValue(forwarder.Definition.Key, out var index))
                Add((uint)index.Value, "jsimport-forwarder:" + forwarder.CanonicalName);
        }
        if (indices.ManagedThrowFunctionIndex.IsPresent)
            Add((uint)indices.ManagedThrowFunctionIndex.Value, "managed-throw");
        if (indices.ManagedRethrowFunctionIndex.IsPresent)
            Add((uint)indices.ManagedRethrowFunctionIndex.Value, "managed-rethrow");
        foreach (var guard in data.StaticInitializerGuards.Values)
        {
            if (!guard.FunctionIndex.IsPresent)
                continue;
            var initializer = guard.Direct is EntityKey key
                ? canonicalByKey.GetValueOrDefault(key)
                : guard.Constructed;
            if (initializer is not null)
                Add((uint)guard.FunctionIndex.Value, "static-initializer:" + initializer);
        }
        var named = byIndex
            .Where(pair => !ambiguousIndices.Contains(pair.Key) &&
                !ambiguousSymbols.Contains(pair.Value))
            .ToFrozenDictionary();
        return new(named, named.ToFrozenDictionary(
            pair => pair.Value, pair => pair.Key, StringComparer.Ordinal));
    }
}
