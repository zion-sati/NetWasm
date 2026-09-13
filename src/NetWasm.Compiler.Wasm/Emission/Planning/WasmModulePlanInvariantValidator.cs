using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core.IntermediateRepresentation.Identity;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IWasmModulePlanInvariantValidator
{
    void Validate(
        WasmEmissionRequest request,
        ImmutableArray<StructuredMethodEmission> methodEmissions,
        WasmModulePlan plan);
}

internal sealed class WasmModulePlanInvariantValidator :
    IWasmModulePlanInvariantValidator
{
    public void Validate(
        WasmEmissionRequest request,
        ImmutableArray<StructuredMethodEmission> methodEmissions,
        WasmModulePlan plan)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(plan);
        var indices = new HashSet<int>();
        var expected = plan.RuntimeImports.Length + plan.InteropImports.Imports.Length;
        foreach (var method in request.JSImportMethods)
        {
            if (!plan.FunctionIndices.ImportedMethods.TryGetValue(method.Key, out var index))
            {
                throw Invalid($"import '{method.Key}' has no function index");
            }
            RequireIndex(index.Value, expected++, indices, "import");
        }
        foreach (var group in request.WitImportMethods
                     .GroupBy(method => method.WitImport!.Identity))
        {
            var groupIndex = expected++;
            foreach (var method in group)
            {
                if (!plan.FunctionIndices.ImportedMethods.TryGetValue(
                        method.Key,
                        out var index))
                {
                    throw Invalid($"import '{method.Key}' has no function index");
                }
                if (index.Value != groupIndex)
                {
                    throw Invalid(
                        $"canonical import '{group.Key}' does not share function index {groupIndex}");
                }
            }
            RequireIndex(groupIndex, groupIndex, indices, "canonical import");
        }
        var constructedIdentities = request.ConstructedMethods.Keys
            .ToHashSet(StringComparer.Ordinal);
        var expectedMethods = methodEmissions
            .Where(method => !constructedIdentities.Contains(method.Identity.CanonicalName))
            .Select(method => new ManagedDefinitionEmission(
                method.Identity,
                method.Method.Header.Method.Key))
            .OrderBy(method => method.Identity.CanonicalName, StringComparer.Ordinal)
            .ToArray();
        if (!plan.OrderedMethods.SequenceEqual(expectedMethods))
        {
            throw Invalid("direct method order is not canonical");
        }
        foreach (var method in plan.OrderedMethods)
        {
            if (!plan.FunctionIndices.DirectMethods.TryGetValue(method.MethodKey, out var index))
            {
                throw Invalid($"direct method '{method}' has no function index");
            }
            RequireIndex(index.Value, expected++, indices, "direct method");
        }
        var expectedConstructed = request.ConstructedMethods.Keys
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(key => new ManagedMethodIdentity(key))
            .ToArray();
        if (!plan.OrderedConstructedMethods.SequenceEqual(expectedConstructed))
        {
            throw Invalid("constructed method order is not canonical");
        }
        foreach (var method in plan.OrderedConstructedMethods)
        {
            if (!plan.FunctionIndices.ConstructedMethods.TryGetValue(method.CanonicalName, out var index))
            {
                throw Invalid($"constructed method '{method}' has no function index");
            }
            RequireIndex(index.Value, expected++, indices, "constructed method");
        }
        foreach (var invoke in plan.DelegateInvokes)
        {
            var index = plan.FunctionIndices.GetDelegateInvokeHelper(
                invoke.DeclaringType.CanonicalName);
            RequireIndex(index.Value, expected++, indices, "delegate invoke helper");
        }
        RequireOptional(plan.DelegateCountHelperIndex, ref expected, indices);
        RequireOptional(plan.DelegateLeafHelperIndex, ref expected, indices);
        RequireOptional(plan.DelegateEqualityHelperIndex, ref expected, indices);
        RequireOptional(plan.DelegateRemoveHelperIndex, ref expected, indices);
        if (plan.FunctionIndices.DirectMethods.Count != plan.OrderedMethods.Length ||
            plan.FunctionIndices.ConstructedMethods.Count !=
                plan.OrderedConstructedMethods.Length ||
            plan.FunctionIndices.DelegateInvokeHelpers.Count !=
                plan.DelegateInvokes.Length ||
            plan.FunctionIndices.ImportedMethods.Count !=
                request.JSImportMethods.Length + request.WitImportMethods.Length)
        {
            throw Invalid("function-index maps contain an omitted or unplanned entry");
        }
    }

    private static void RequireOptional(
        OptionalFunctionIndex index,
        ref int expected,
        HashSet<int> indices)
    {
        if (!index.IsPresent)
        {
            return;
        }
        RequireIndex(index.Value, expected++, indices, "delegate helper");
    }

    private static void RequireIndex(
        int actual,
        int expected,
        HashSet<int> indices,
        string kind)
    {
        if (actual < 0 || actual != expected || !indices.Add(actual))
        {
            throw Invalid(
                $"{kind} function index {actual} is negative, duplicated, or not the expected index {expected}");
        }
    }

    private static InvalidOperationException Invalid(string message) => new(
        $"Compiler invariant failed before Wasm serialization: {message}.");
}
