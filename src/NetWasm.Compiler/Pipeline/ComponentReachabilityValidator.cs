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
            .Where(method => method.WitExport is { IsComponentLift: false })
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
            .Where(method => !method.WitImport!.IsCanonicalBuiltIn &&
                !declaredImports.Contains(method.WitImport.Identity))
            .Select(method => method.Key)
            .ToArray();
        var missingExports = reachableExports.Except(declaredExports).ToArray();

        if (missingImports.Length != 0 || missingExports.Length != 0)
        {
            var missing = missingImports.Concat(missingExports).ToArray();
            // Bindings generated before world keys name an interface
            // `namespace:package@version/interface`, which no world item matches.
            var packageFirst = missing
                .Where(binding => IsPackageFirstKey(InterfaceName(methods.GetMethod(binding))))
                .ToArray();
            if (packageFirst.Length != 0)
            {
                var binding = FormatFirstBinding(packageFirst, methods, symbols);
                var method = packageFirst.Select(methods.GetMethod).First(method => symbols.Format(method) == binding);
                throw new CompilerException(new CompilerDiagnostic(
                    DiagnosticCode.ComponentContract,
                    $"reachable WIT binding names interface '{InterfaceName(method)}' by the package-first key of " +
                    "bindings generated before world keys; regenerate the bindings with this NetWasm's " +
                    "netwasm-wit-bindgen or NetWasmWit item, or update the package that contains them",
                    binding));
            }
            var first = FormatFirstBinding(missing, methods, symbols);
            var missingMethod = missing.Select(methods.GetMethod).First(method => symbols.Format(method) == first);
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.ComponentContract,
                Missing(missingMethod, contract),
                first));
        }
    }

    // What the program reaches and the world lacks, and the line that adds it, or the method that
    // reaches nothing the world declares (issue 180).
    private static string Missing(MethodDefinitionModel method, ComponentBoundaryContract contract)
    {
        var (verb, interfaceName, function) = method.WitImport is { } import
            ? ("import", import.InterfaceName, import.FunctionName)
            : ("export", method.WitExport!.InterfaceName, method.WitExport.FunctionName);
        var world = contract.World.Length == 0 ? "the selected world" : $"world '{WorldName(contract)}'";
        return interfaceName.Length == 0
            ? $"reachable WIT binding {verb}s function '{function}', which {world} does not {verb}; " +
                $"add it to the world's {verb}s, or remove the code that reaches it"
            : $"reachable WIT binding {verb}s '{interfaceName}', which {world} does not {verb}; " +
                $"add `{verb} {interfaceName};` to the world, or remove the code that reaches it";
    }

    // namespace:package/world@version, as a world is selected.
    private static string WorldName(ComponentBoundaryContract contract)
    {
        var at = contract.Package.IndexOf('@');
        return at < 0
            ? $"{contract.Package}/{contract.World}"
            : $"{contract.Package[..at]}/{contract.World}{contract.Package[at..]}";
    }

    // namespace:package@version/interface, the key bindings generated before
    // world keys name an interface by.
    internal static bool IsPackageFirstKey(string name)
    {
        var slash = name.IndexOf('/');
        if (slash < 0 || slash == name.Length - 1 || name.IndexOf('/', slash + 1) >= 0)
            return false;
        var at = name.IndexOf('@', 0, slash);
        return at >= 3 && at < slash - 1 && name.AsSpan(1, at - 2).Contains(':');
    }

    // A missing binding is a WIT import or export.
    private static string InterfaceName(MethodDefinitionModel method) =>
        method.WitImport?.InterfaceName ?? method.WitExport!.InterfaceName;

    private static string FormatFirstBinding(
        IEnumerable<EntityKey> bindings,
        IMethodRepository methods,
        ISymbolFormatter symbols) => bindings
        .Select(methods.GetMethod)
        .Select(symbols.Format)
        .Order(StringComparer.Ordinal)
        .First();
}
