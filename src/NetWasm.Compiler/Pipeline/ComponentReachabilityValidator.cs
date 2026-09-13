using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Pipeline;

internal interface IComponentReachabilityValidator
{
    void Validate(
        ReachableProgram program,
        ImmutableArray<ProgramExport> exports,
        ComponentBoundaryContract contract,
        IMethodRepository methods,
        ISymbolFormatter symbols);
}

internal sealed class ComponentReachabilityValidator : IComponentReachabilityValidator
{
    public void Validate(
        ReachableProgram program,
        ImmutableArray<ProgramExport> exports,
        ComponentBoundaryContract contract,
        IMethodRepository methods,
        ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(methods);
        ArgumentNullException.ThrowIfNull(symbols);

        var reachableImports = program.WitImportMethods;
        var reachableExports = exports
            .Select(export => methods.GetMethod(export.Method))
            .Where(method => method.WitExport is not null)
            .Select(method => method.Key)
            .ToImmutableHashSet();
        if (reachableImports.Length == 0 && reachableExports.Count == 0)
        {
            return;
        }
        if (contract.IsEmpty)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.ComponentContract,
                "reachable WIT imports or exports require '--wit' and a selected WIT world",
                FormatFirstBinding(
                    reachableImports.Select(method => method.Key).Concat(reachableExports),
                    methods,
                    symbols)));
        }
        var declaredImports = contract.Imports
            .Where(function => function.HasManagedBinding)
            .Select(function => function.Identity)
            .ToImmutableHashSet();
        var declaredExports = contract.Exports.Select(function => function.ManagedMethod)
            .ToImmutableHashSet();
        var missingImports = reachableImports
            .Where(method => !declaredImports.Contains(method.WitImport!.Identity))
            .Select(method => method.Key)
            .ToArray();
        var missingExports = reachableExports.Except(declaredExports).ToArray();

        if (missingImports.Length != 0 || missingExports.Length != 0)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.ComponentContract,
                "reachable WIT binding is not part of the selected world",
                FormatFirstBinding(
                    missingImports.Concat(missingExports),
                    methods,
                    symbols)));
        }
    }

    private static string FormatFirstBinding(
        IEnumerable<EntityKey> bindings,
        IMethodRepository methods,
        ISymbolFormatter symbols) => bindings
        .Select(methods.GetMethod)
        .Select(symbols.Format)
        .Order(StringComparer.Ordinal)
        .First();
}
