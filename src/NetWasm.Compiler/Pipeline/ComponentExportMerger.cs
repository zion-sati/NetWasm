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

        var result = exports.ToBuilder();
        var methodsByName = exports.ToDictionary(
            export => export.Name,
            export => export.Method,
            StringComparer.Ordinal);
        foreach (var function in contract.Exports)
        {
            var name = CanonicalAbiNames.Export(function, target);
            if (methodsByName.TryGetValue(name, out var existing))
            {
                if (existing != function.ManagedMethod)
                {
                    throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.ComponentContract,
                        $"component export '{name}' has conflicting managed bindings"));
                }
                continue;
            }

            methodsByName.Add(name, function.ManagedMethod);
            result.Add(new ProgramExport(name, function.ManagedMethod));
        }
        return result.ToImmutable();
    }
}
