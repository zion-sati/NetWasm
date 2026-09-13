using System;
using System.Collections.Generic;
using System.Linq;

namespace NetWasm.Compiler.Wasm.Emission;

public sealed record ManagedBoundaryPlanEntry(
    int FunctionIndex,
    string GeneratedFunctionName,
    string ExportName,
    ManagedBoundaryKind Kind,
    bool IsOutwardFacing,
    ManagedBoundaryFailureDisposition Disposition);

public sealed class ManagedBoundaryPlanValidator : IManagedBoundaryPlanValidator
{
    private readonly IManagedBoundaryFailureDispositionResolver _policy;

    public ManagedBoundaryPlanValidator(
        IManagedBoundaryFailureDispositionResolver policy)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public void Validate(
        IReadOnlyList<WasmFunctionDefinition> functions,
        IReadOnlyList<WasmExport> exports,
        int importedFunctionCount,
        IEnumerable<ManagedBoundaryPlanEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(exports);
        ArgumentNullException.ThrowIfNull(entries);

        var materialized = entries.ToArray();
        var duplicate = materialized
            .GroupBy(entry => entry.FunctionIndex)
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Generated function index '{duplicate.Key}' has more than one managed-boundary policy assignment.");

        var functionExports = exports.Where(export => export.Kind == WasmExportKind.Function).ToArray();
        var omitted = functionExports.FirstOrDefault(export =>
            materialized.All(entry => entry.FunctionIndex != export.Index));
        if (omitted is not null)
            throw new InvalidOperationException($"Exported function '{omitted.Name}' has no managed-boundary policy assignment.");

        var unexported = materialized.FirstOrDefault(entry =>
            entry.IsOutwardFacing && functionExports.All(export => export.Index != entry.FunctionIndex));
        if (unexported is not null)
            throw new InvalidOperationException($"Outward managed boundary '{unexported.GeneratedFunctionName}' is not exported.");

        foreach (var entry in materialized)
        {
            if (string.IsNullOrWhiteSpace(entry.GeneratedFunctionName))
                throw new InvalidOperationException("Every generated managed boundary must have a stable function name.");
            if (entry.FunctionIndex < importedFunctionCount ||
                entry.FunctionIndex - importedFunctionCount >= functions.Count)
                throw new InvalidOperationException($"Managed boundary '{entry.GeneratedFunctionName}' has an invalid function index.");
            if (!StringComparer.Ordinal.Equals(
                    functions[entry.FunctionIndex - importedFunctionCount].Name,
                    entry.GeneratedFunctionName))
                throw new InvalidOperationException($"Managed boundary index for '{entry.GeneratedFunctionName}' does not identify that generated function.");
            if (!functionExports.Any(export =>
                    export.Index == entry.FunctionIndex &&
                    StringComparer.Ordinal.Equals(export.Name, entry.ExportName)))
                throw new InvalidOperationException($"Managed boundary '{entry.GeneratedFunctionName}' does not identify its actual export.");

            var expected = _policy.Resolve(entry.Kind);
            if (entry.Disposition != expected)
                throw new InvalidOperationException($"Generated function '{entry.GeneratedFunctionName}' has disposition '{entry.Disposition}', expected '{expected}'.");

            if (!entry.IsOutwardFacing && entry.Disposition == ManagedBoundaryFailureDisposition.ReportAndTerminate)
                throw new InvalidOperationException($"Internal generated function '{entry.GeneratedFunctionName}' cannot own terminal reporting.");
        }
    }
}
