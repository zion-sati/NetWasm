using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawCoreRuntimeImportDeclarationRequest(
    RawWitImportCatalog ApplicationCatalog,
    RawWitImportCatalog RuntimeCatalog,
    RawCompilerImportDeclarations CompilerDeclarations,
    ImmutableArray<RawCoreFunctionImportSignature> RuntimeImports,
    ImmutableArray<RawCanonicalImportIdentity> FinalImports);

public interface IRawCoreRuntimeImportDeclarationBuilder
{
    ImmutableDictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature> Build(
        RawCoreRuntimeImportDeclarationRequest request);
}

public sealed class RawCoreRuntimeImportDeclarationBuilder(
    IRawWitBindingPlanBuilder plans,
    IRawCliCoreSignatureProjector signatures) : IRawCoreRuntimeImportDeclarationBuilder
{
    private readonly IRawWitBindingPlanBuilder _plans = plans ?? throw new ArgumentNullException(nameof(plans));
    private readonly IRawCliCoreSignatureProjector _signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));

    public ImmutableDictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature> Build(
        RawCoreRuntimeImportDeclarationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ApplicationCatalog);
        ArgumentNullException.ThrowIfNull(request.RuntimeCatalog);
        ArgumentNullException.ThrowIfNull(request.ApplicationCatalog.Imports);
        ArgumentNullException.ThrowIfNull(request.RuntimeCatalog.Imports);
        ArgumentNullException.ThrowIfNull(request.CompilerDeclarations);
        ArgumentNullException.ThrowIfNull(request.CompilerDeclarations.JavaScriptImports);
        ArgumentNullException.ThrowIfNull(request.CompilerDeclarations.RuntimeImports);
        var target = request.ApplicationCatalog.Target;
        if (target is not (WasmTarget.Wasm32 or WasmTarget.Wasm64)
            || request.RuntimeCatalog.Target != target)
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        if (request.RuntimeImports.IsDefault || request.FinalImports.IsDefault)
        {
            throw Invalid("runtime and final import observations must be explicit");
        }

        var observed = new HashSet<RawCanonicalImportIdentity>();
        foreach (var import in request.RuntimeImports)
        {
            ArgumentNullException.ThrowIfNull(import);
            RequireIdentity(import.Identity);
            if (import.Parameters.IsDefault || import.Results.IsDefault
                || import.Parameters.Any(value => !Enum.IsDefined(value))
                || import.Results.Any(value => !Enum.IsDefined(value)))
            {
                throw Invalid("runtime import has an invalid core signature");
            }
            if (!observed.Add(import.Identity))
            {
                throw Invalid("runtime module imports contain duplicate physical identities");
            }
            if (request.CompilerDeclarations.JavaScriptImports.ContainsKey(import.Identity))
            {
                throw Invalid("runtime module import has a JavaScript owner");
            }
        }
        var reached = new HashSet<RawCanonicalImportIdentity>(observed);
        foreach (var identity in request.FinalImports)
        {
            RequireIdentity(identity);
            reached.Add(identity);
        }
        foreach (var identity in reached)
        {
            if (request.RuntimeCatalog.Imports.ContainsKey(identity)
                && request.CompilerDeclarations.JavaScriptImports.ContainsKey(identity))
            {
                throw Invalid("runtime WIT import has a JavaScript owner");
            }
        }

        var application = ProjectWit(request.ApplicationCatalog, reached);
        var runtime = ProjectWit(request.RuntimeCatalog, reached);
        var compiler = request.CompilerDeclarations.RuntimeImports
            .Where(pair => reached.Contains(pair.Key))
            .ToImmutableDictionary(pair => pair.Key, pair => _signatures.Project(pair.Value, target));
        foreach (var pair in runtime)
        {
            if (application.TryGetValue(pair.Key, out var applicationSignature))
            {
                RequireMatching(pair.Value, applicationSignature);
            }
            if (compiler.TryGetValue(pair.Key, out var compilerSignature))
            {
                RequireMatching(pair.Value, compilerSignature);
            }
        }
        foreach (var import in request.RuntimeImports)
        {
            if (application.TryGetValue(import.Identity, out var applicationSignature))
            {
                RequireMatching(import, applicationSignature);
            }
            if (runtime.TryGetValue(import.Identity, out var runtimeSignature))
            {
                RequireMatching(import, runtimeSignature);
            }
            if (compiler.TryGetValue(import.Identity, out var compilerSignature))
            {
                RequireMatching(import, compilerSignature);
            }
        }
        return runtime.Where(pair => !request.ApplicationCatalog.Imports.ContainsKey(pair.Key))
            .ToImmutableDictionary();
    }

    private ImmutableDictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature> ProjectWit(
        RawWitImportCatalog catalog, HashSet<RawCanonicalImportIdentity> reached)
    {
        var plan = _plans.Build(new(catalog,
            [.. reached.Where(catalog.Imports.ContainsKey)
                .OrderBy(identity => identity.Module, StringComparer.Ordinal)
                .ThenBy(identity => identity.Name, StringComparer.Ordinal)],
            ImmutableHashSet<RawCanonicalImportIdentity>.Empty,
            ImmutableHashSet<RawCanonicalImportIdentity>.Empty));
        return plan.Imports.ToImmutableDictionary(layout => layout.Identity,
            layout => _signatures.Project(new(layout.Identity, layout.Signature.Parameters,
                layout.Signature.Result), catalog.Target));
    }

    private static void RequireIdentity(RawCanonicalImportIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Module);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
    }

    private static void RequireMatching(RawCoreFunctionImportSignature actual, RawCoreFunctionImportSignature expected)
    {
        if (!actual.Parameters.SequenceEqual(expected.Parameters)
            || !actual.Results.SequenceEqual(expected.Results))
        {
            throw Invalid("independently declared runtime import signatures disagree");
        }
    }

    private static CompilerException Invalid(string message) => new(
        new CompilerDiagnostic(DiagnosticCode.ComponentContract, message));
}
