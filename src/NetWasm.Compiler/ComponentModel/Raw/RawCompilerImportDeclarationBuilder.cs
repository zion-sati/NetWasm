using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.ComponentModel.Raw;

public sealed record RawCompilerImportDeclarationRequest(
    RawCompilerImportSource Source,
    RawWitImportCatalog WitCatalog);

public sealed record RawCompilerImportSource(
    ImmutableArray<WasmFunctionImport> FunctionImports,
    HostInteropManifest InteropManifest);

public sealed record RawCompilerImportDeclarations(
    ImmutableDictionary<RawCanonicalImportIdentity, RawCliFunctionImportSignature> JavaScriptImports,
    ImmutableDictionary<RawCanonicalImportIdentity, RawCliFunctionImportSignature> RuntimeImports);

public interface IRawCompilerImportDeclarationBuilder
{
    RawCompilerImportDeclarations Build(RawCompilerImportDeclarationRequest request);
}

public sealed class RawCompilerImportDeclarationBuilder : IRawCompilerImportDeclarationBuilder
{
    public RawCompilerImportDeclarations Build(RawCompilerImportDeclarationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Source);
        ArgumentNullException.ThrowIfNull(request.Source.InteropManifest);
        ArgumentNullException.ThrowIfNull(request.WitCatalog);
        ArgumentNullException.ThrowIfNull(request.WitCatalog.Imports);
        RequireExplicit(request.Source.FunctionImports, "compiler function imports");
        RequireExplicit(request.Source.InteropManifest.Imports, "host interop imports");
        ValidateTarget(request.Source.InteropManifest, request.WitCatalog.Target);

        var javaScriptIdentities = BuildJavaScriptIdentities(
            request.Source.InteropManifest.Imports,
            request.WitCatalog.Imports);
        var missingJavaScriptImports = javaScriptIdentities.ToHashSet();
        var javaScript = ImmutableDictionary.CreateBuilder<
            RawCanonicalImportIdentity,
            RawCliFunctionImportSignature>();
        var runtime = ImmutableDictionary.CreateBuilder<
            RawCanonicalImportIdentity,
            RawCliFunctionImportSignature>();
        var seen = new HashSet<RawCanonicalImportIdentity>();

        foreach (var import in request.Source.FunctionImports)
        {
            var declaration = CreateDeclaration(import);
            if (!seen.Add(declaration.Identity))
            {
                throw Invalid("compiler function imports contain duplicate physical identities");
            }
            if (request.WitCatalog.Imports.ContainsKey(declaration.Identity))
            {
                continue;
            }
            if (javaScriptIdentities.Contains(declaration.Identity))
            {
                javaScript.Add(declaration.Identity, declaration);
                missingJavaScriptImports.Remove(declaration.Identity);
                continue;
            }
            runtime.Add(declaration.Identity, declaration);
        }
        if (missingJavaScriptImports.Count != 0)
        {
            throw Invalid("host interop manifest import is absent from compiler function imports");
        }
        return new(javaScript.ToImmutable(), runtime.ToImmutable());
    }

    private static ImmutableHashSet<RawCanonicalImportIdentity> BuildJavaScriptIdentities(
        ImmutableArray<HostInteropImport> imports,
        ImmutableDictionary<RawCanonicalImportIdentity, RawWitImportDeclaration> witImports)
    {
        var identities = ImmutableHashSet.CreateBuilder<RawCanonicalImportIdentity>();
        foreach (var import in imports)
        {
            ArgumentNullException.ThrowIfNull(import);
            var identity = CreateIdentity(import.Module, import.Name);
            if (witImports.ContainsKey(identity))
            {
                throw Invalid("raw import has both WIT and JavaScript owners");
            }
            if (!identities.Add(identity))
            {
                throw Invalid("host interop manifest contains duplicate physical identities");
            }
        }
        return identities.ToImmutable();
    }

    private static RawCliFunctionImportSignature CreateDeclaration(WasmFunctionImport import)
    {
        ArgumentNullException.ThrowIfNull(import);
        ArgumentNullException.ThrowIfNull(import.Type);
        RequireExplicit(import.Type.Parameters, "compiler function import parameters");
        if (import.Type.Parameters.Any(kind => !Enum.IsDefined(kind) || kind == CliValueKind.Void)
            || !Enum.IsDefined(import.Type.Result))
        {
            throw Invalid("compiler function import contains an unsupported stack kind");
        }
        return new(
            CreateIdentity(import.Module, import.Name),
            import.Type.Parameters,
            import.Type.Result);
    }

    private static RawCanonicalImportIdentity CreateIdentity(string module, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new(module, name);
    }

    private static void ValidateTarget(HostInteropManifest manifest, WasmTarget target)
    {
        var expected = target == WasmTarget.Wasm64 ? "wasm64" : "wasm32";
        if (target is not (WasmTarget.Wasm32 or WasmTarget.Wasm64))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
        if (!string.Equals(manifest.Target, expected, StringComparison.Ordinal))
        {
            throw Invalid("host interop manifest target does not match the raw WIT target");
        }
    }

    private static void RequireExplicit<T>(ImmutableArray<T> values, string label)
    {
        if (values.IsDefault)
        {
            throw Invalid($"{label} must be explicit");
        }
    }

    private static CompilerException Invalid(string message) => new(
        new CompilerDiagnostic(DiagnosticCode.ComponentContract, message));
}
