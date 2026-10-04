using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

internal interface IComponentExportMerger
{
    ImmutableArray<ProgramExport> Merge(
        ImmutableArray<ProgramExport> exports,
        ComponentBoundaryContract contract,
        WasmTarget target);
}

internal sealed class ComponentExportMerger : IComponentExportMerger
{
    public ImmutableArray<ProgramExport> Merge(
        ImmutableArray<ProgramExport> exports,
        ComponentBoundaryContract contract,
        WasmTarget target)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (contract.Exports.IsEmpty)
        {
            return exports;
        }

        var existingMethods = exports.ToDictionary(
            export => export.Name,
            export => export.Method,
            StringComparer.Ordinal);
        var replacements = contract.Exports
            .SelectMany(function => ReplacedBindings(function, target))
            .ToArray();
        foreach (var replacement in replacements)
        {
            if (existingMethods.TryGetValue(replacement.Name, out var existing) &&
                existing != replacement.Method)
            {
                throw Conflict(replacement.Name);
            }
        }
        var replacedNames = replacements
            .Select(replacement => replacement.Name)
            .ToImmutableHashSet(StringComparer.Ordinal);
        var result = exports
            .Where(export => !replacedNames.Contains(export.Name))
            .ToImmutableArray()
            .ToBuilder();
        var methodsByName = result.ToDictionary(
            export => export.Name,
            export => export.Method,
            StringComparer.Ordinal);
        foreach (var function in contract.Exports)
        {
            Add(
                CanonicalAbiNames.Export(function, target),
                function.ManagedMethod);
            if (function.PostReturnMethod is { } postReturn)
            {
                Add(CanonicalAbiNames.PostReturn(function, target), postReturn);
            }
        }
        return result.ToImmutable();

        void Add(string name, EntityKey method)
        {
            if (methodsByName.TryGetValue(name, out var existing))
            {
                if (existing != method)
                {
                    throw Conflict(name);
                }
                return;
            }
            methodsByName.Add(name, method);
            result.Add(new ProgramExport(name, method));
        }
    }

    private static CompilerException Conflict(string name) => new(
        new CompilerDiagnostic(
            DiagnosticCode.ComponentContract,
            $"component export '{name}' has conflicting managed bindings"));

    private static IEnumerable<(string Name, EntityKey Method)> ReplacedBindings(
        CanonicalAbiFunction function,
        WasmTarget target)
    {
        var physical = CanonicalAbiNames.Export(function, target);
        var semantic = CanonicalAbiNames.Export(
            function.InterfaceName,
            function.FunctionName,
            target);
        if (!string.Equals(physical, semantic, StringComparison.Ordinal))
        {
            yield return (semantic, function.ManagedMethod);
        }
        if (function.PostReturnMethod is not null)
        {
            var physicalPostReturn = CanonicalAbiNames.PostReturn(function, target);
            var semanticPostReturn = CanonicalAbiNames.PostReturn(
                function.InterfaceName,
                function.FunctionName,
                target);
            if (!string.Equals(physicalPostReturn, semanticPostReturn,
                StringComparison.Ordinal))
            {
                yield return (semanticPostReturn, function.PostReturnMethod.Value);
            }
        }
    }
}
