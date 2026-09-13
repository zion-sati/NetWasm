using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawDeploymentBindingProjection(
    ImmutableArray<RawValidatedBindingPlan> AdapterPlans,
    ImmutableArray<WitInterfaceFunction> RequiredImports);

public interface IRawDeploymentFunctionProjector
{
    RawDeploymentBindingProjection Project(
        RawValidatedBindingPlan plan,
        string runtimeWitPath,
        string? runtimeWorld);
}

public sealed class RawDeploymentFunctionProjector(
    IWitDocumentReader documents,
    IRawWitImportCatalogBuilder catalogs,
    IRawWitBindingPlanBuilder plans,
    IRawFinalImportSignatureValidator signatures,
    IWitTypeIdentityFormatter types) : IRawDeploymentFunctionProjector
{
    private readonly IWitDocumentReader _documents = documents ??
        throw new ArgumentNullException(nameof(documents));
    private readonly IRawWitImportCatalogBuilder _catalogs = catalogs ??
        throw new ArgumentNullException(nameof(catalogs));
    private readonly IRawWitBindingPlanBuilder _plans = plans ??
        throw new ArgumentNullException(nameof(plans));
    private readonly IRawFinalImportSignatureValidator _signatures = signatures ??
        throw new ArgumentNullException(nameof(signatures));
    private readonly IWitTypeIdentityFormatter _types = types ??
        throw new ArgumentNullException(nameof(types));

    public RawDeploymentBindingProjection Project(
        RawValidatedBindingPlan plan,
        string runtimeWitPath,
        string? runtimeWorld)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeWitPath);

        var runtimeDocument = _documents.Read(runtimeWitPath);
        ArgumentNullException.ThrowIfNull(runtimeDocument);
        var runtimeCatalog = _catalogs.Build(
            runtimeDocument,
            runtimeDocument.SelectWorld(runtimeWorld),
            plan.Plan.Selection.Catalog.Target);
        var applicationBindings = plan.Plan.Selection.WitImports.ToImmutableHashSet();
        var runtimeObserved = plan.ObservedImports
            .Where(import => runtimeCatalog.Imports.ContainsKey(import.Identity)
                && !applicationBindings.Contains(import.Identity))
            .ToImmutableArray();
        var runtimePlan = _plans.Build(new(
            runtimeCatalog,
            [.. runtimeObserved.Select(import => import.Identity)],
            ImmutableHashSet<RawCanonicalImportIdentity>.Empty,
            ImmutableHashSet<RawCanonicalImportIdentity>.Empty));
        var validatedRuntime = _signatures.Validate(new(
            runtimePlan,
            [],
            [],
            runtimeObserved));

        return new(
            [plan, validatedRuntime],
            Merge(ProjectFunctions(plan), ProjectFunctions(validatedRuntime)));
    }

    private ImmutableArray<WitInterfaceFunction> ProjectFunctions(
        RawValidatedBindingPlan plan)
    {
        var document = plan.Plan.Selection.Catalog.Document;
        return
        [
            .. plan.Plan.Imports
                .OfType<RawWitImportLayout.Callable>()
                .Select(import => Project(document, plan, import)),
        ];
    }

    private WitInterfaceFunction Project(
        WitDocument document,
        RawValidatedBindingPlan validated,
        RawWitImportLayout.Callable import)
    {
        if (!validated.Plan.Selection.Catalog.Imports.TryGetValue(
                import.Identity,
                out var candidate)
            || candidate is not RawWitImportDeclaration.Callable declaration
            || !ReferenceEquals(declaration.Definition, import.Function.Declaration)
            || string.IsNullOrWhiteSpace(declaration.DeploymentInterfaceName))
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.ComponentContract,
                "deployment imports must retain their selected versioned WIT identity"));
        }

        return new(
            declaration.DeploymentInterfaceName,
            import.Function.Declaration.Name,
            [.. import.Function.Declaration.Parameters.Select(parameter =>
                _types.Format(document, parameter.Type))],
            import.Function.Declaration.Result is null
                ? []
                : [_types.Format(document, import.Function.Declaration.Result)]);
    }

    private static ImmutableArray<WitInterfaceFunction> Merge(
        ImmutableArray<WitInterfaceFunction> application,
        ImmutableArray<WitInterfaceFunction> runtime)
    {
        var functions = new Dictionary<(string Interface, string Name),
            WitInterfaceFunction>();
        foreach (var function in application.Concat(runtime))
        {
            ArgumentNullException.ThrowIfNull(function);
            var key = (function.Interface, function.Name);
            if (functions.TryGetValue(key, out var existing))
            {
                if (!existing.Parameters.SequenceEqual(function.Parameters)
                    || !existing.Results.SequenceEqual(function.Results))
                {
                    throw new CompilerException(new CompilerDiagnostic(
                        DiagnosticCode.ComponentContract,
                        "application and runtime WIT imports disagree"));
                }
                continue;
            }
            functions.Add(key, function);
        }
        return
        [
            .. functions.Values
                .OrderBy(function => function.Interface, StringComparer.Ordinal)
                .ThenBy(function => function.Name, StringComparer.Ordinal),
        ];
    }
}
