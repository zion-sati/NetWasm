using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawBuildImportValidationRequest(
    CompilationResult Compilation,
    string WitPath,
    string? World,
    WasmTarget Target,
    RawModuleInspectionRequest RuntimeInspection,
    RawModuleInspectionRequest FinalInspection)
{
    public string RuntimeWitPath { get; init; } = WitPath;

    public string? RuntimeWorld { get; init; } = World;
}

public sealed record RawBuildImportSourceValidationRequest(
    RawCompilerImportSource Source,
    string WitPath,
    string? World,
    WasmTarget Target,
    RawModuleInspectionRequest RuntimeInspection,
    RawModuleInspectionRequest FinalInspection)
{
    public string RuntimeWitPath { get; init; } = WitPath;

    public string? RuntimeWorld { get; init; } = World;
}

public interface IRawBuildImportValidator
{
    RawValidatedBindingPlan Validate(RawBuildImportValidationRequest request);
}

public interface IRawBuildImportSourceValidator
{
    RawValidatedBindingPlan Validate(RawBuildImportSourceValidationRequest request);
}

public sealed class RawBuildImportValidator(
    IRawBuildImportSourceValidator sources) : IRawBuildImportValidator
{
    private readonly IRawBuildImportSourceValidator _sources = sources ??
        throw new ArgumentNullException(nameof(sources));

    public RawValidatedBindingPlan Validate(RawBuildImportValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Compilation);
        return _sources.Validate(new RawBuildImportSourceValidationRequest(
            new(
                request.Compilation.FunctionImports,
                request.Compilation.InteropManifest),
            request.WitPath,
            request.World,
            request.Target,
            request.RuntimeInspection,
            request.FinalInspection)
        {
            RuntimeWitPath = request.RuntimeWitPath,
            RuntimeWorld = request.RuntimeWorld,
        });
    }
}

public sealed class RawBuildImportSourceValidator(
    IWitDocumentReader documents,
    IRawWitImportCatalogBuilder catalogs,
    IRawCompilerImportDeclarationBuilder declarations,
    IRawModuleImportSignatureReader modules,
    IRawWitBindingPlanBuilder plans,
    IRawFinalImportSignatureValidator signatures,
    IRawCoreRuntimeImportDeclarationBuilder runtimeDeclarations) : IRawBuildImportSourceValidator
{
    private readonly IWitDocumentReader _documents = documents ??
        throw new ArgumentNullException(nameof(documents));
    private readonly IRawWitImportCatalogBuilder _catalogs = catalogs ??
        throw new ArgumentNullException(nameof(catalogs));
    private readonly IRawCompilerImportDeclarationBuilder _declarations = declarations ??
        throw new ArgumentNullException(nameof(declarations));
    private readonly IRawModuleImportSignatureReader _modules = modules ??
        throw new ArgumentNullException(nameof(modules));
    private readonly IRawWitBindingPlanBuilder _plans = plans ??
        throw new ArgumentNullException(nameof(plans));
    private readonly IRawFinalImportSignatureValidator _signatures = signatures ??
        throw new ArgumentNullException(nameof(signatures));
    private readonly IRawCoreRuntimeImportDeclarationBuilder _runtimeDeclarations = runtimeDeclarations ??
        throw new ArgumentNullException(nameof(runtimeDeclarations));

    public RawValidatedBindingPlan Validate(RawBuildImportSourceValidationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentNullException.ThrowIfNull(request.RuntimeInspection);
        ArgumentNullException.ThrowIfNull(request.FinalInspection);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WitPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RuntimeWitPath);
        if (request.Target is not (WasmTarget.Wasm32 or WasmTarget.Wasm64))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }

        var document = _documents.Read(request.WitPath, request.World);
        ArgumentNullException.ThrowIfNull(document);
        var catalog = _catalogs.Build(
            document,
            document.SelectWorld(request.World),
            request.Target);
        var declared = _declarations.Build(new(request.Source, catalog));
        var runtimeDocument = _documents.Read(request.RuntimeWitPath);
        ArgumentNullException.ThrowIfNull(runtimeDocument);
        var runtimeCatalog = _catalogs.Build(
            runtimeDocument,
            runtimeDocument.SelectWorld(request.RuntimeWorld),
            request.Target);
        var runtimeImports = _modules.Read(request.RuntimeInspection);
        if (runtimeImports.IsDefault)
        {
            throw Invalid("runtime module imports must be explicit");
        }
        var observed = _modules.Read(request.FinalInspection);
        if (observed.IsDefault)
        {
            throw Invalid("observed raw imports must be explicit");
        }
        var coreRuntime = _runtimeDeclarations.Build(new(
            catalog, runtimeCatalog, declared, runtimeImports,
            [.. observed.Select(import => import.Identity)]));
        var plan = _plans.Build(new(
            catalog,
            [.. observed.Select(import => import.Identity)],
            declared.JavaScriptImports.Keys.ToImmutableHashSet(),
            declared.RuntimeImports.Keys.Concat(coreRuntime.Keys).ToImmutableHashSet()));
        return _signatures.Validate(new(
            plan,
            SelectDeclarations(plan.Selection.JavaScriptImports, declared.JavaScriptImports),
            SelectCompilerRuntimeDeclarations(
                plan.Selection.RuntimeImports,
                declared.RuntimeImports,
                coreRuntime),
            observed)
        {
            RuntimeCoreImports = SelectCoreRuntimeDeclarations(
                plan.Selection.RuntimeImports,
                coreRuntime),
        });
    }

    private static ImmutableArray<RawCliFunctionImportSignature> SelectDeclarations(
        ImmutableArray<RawCanonicalImportIdentity> selected,
        ImmutableDictionary<RawCanonicalImportIdentity, RawCliFunctionImportSignature> declarations) =>
        [.. selected.Select(identity => declarations[identity])];

    private static ImmutableArray<RawCliFunctionImportSignature>
        SelectCompilerRuntimeDeclarations(
            ImmutableArray<RawCanonicalImportIdentity> selected,
            ImmutableDictionary<RawCanonicalImportIdentity, RawCliFunctionImportSignature> declarations,
            ImmutableDictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature> coreDeclarations) =>
        [.. selected.Where(identity => !coreDeclarations.ContainsKey(identity))
            .Select(identity => declarations[identity])];

    private static ImmutableArray<RawCoreFunctionImportSignature> SelectCoreRuntimeDeclarations(
        ImmutableArray<RawCanonicalImportIdentity> selected,
        ImmutableDictionary<RawCanonicalImportIdentity, RawCoreFunctionImportSignature> declarations) =>
        [.. selected.Where(declarations.ContainsKey).Select(identity => declarations[identity])];

    private static CompilerException Invalid(string message) => new(
        new CompilerDiagnostic(DiagnosticCode.ComponentContract, message));
}
