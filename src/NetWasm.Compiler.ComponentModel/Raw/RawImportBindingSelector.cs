using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawImportBindingSelector
{
    RawImportBindingSelection SelectBindings(RawImportBindingSelectionRequest request);
}

public sealed class RawImportBindingSelector : IRawImportBindingSelector
{
    public RawImportBindingSelection SelectBindings(RawImportBindingSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Catalog);
        ArgumentNullException.ThrowIfNull(request.Catalog.Imports);
        ArgumentNullException.ThrowIfNull(request.JavaScriptImports);
        ArgumentNullException.ThrowIfNull(request.RuntimeImports);
        if (request.Catalog.Target is not (WasmTarget.Wasm32 or WasmTarget.Wasm64))
        {
            throw new ArgumentOutOfRangeException(nameof(request));
        }
        if (request.FinalImports.IsDefault)
        {
            throw ComponentException.Invalid("final raw imports must be explicit");
        }
        var declaredWit = request.Catalog.Imports.Keys.ToImmutableHashSet();
        var declaredJavaScript = request.JavaScriptImports.WithComparer(EqualityComparer<RawCanonicalImportIdentity>.Default);
        var declaredRuntime = request.RuntimeImports.WithComparer(EqualityComparer<RawCanonicalImportIdentity>.Default);
        var declarations = declaredWit.Concat(declaredJavaScript).Concat(declaredRuntime);
        var declared = new HashSet<RawCanonicalImportIdentity>();
        foreach (var identity in declarations)
        {
            ValidateIdentity(identity);
            if (!declared.Add(identity))
            {
                throw ComponentException.Invalid("raw imports have multiple declared owners");
            }
        }
        var final = new HashSet<RawCanonicalImportIdentity>();
        foreach (var identity in request.FinalImports)
        {
            ValidateIdentity(identity);
            if (!final.Add(identity))
            {
                throw ComponentException.Invalid("final raw imports contain duplicate identities");
            }
            if (!declared.Contains(identity))
            {
                throw ComponentException.Invalid("final raw import has no declared binding");
            }
        }
        var wit = ImmutableArray.CreateBuilder<RawCanonicalImportIdentity>();
        var javascript = ImmutableArray.CreateBuilder<RawCanonicalImportIdentity>();
        var runtime = ImmutableArray.CreateBuilder<RawCanonicalImportIdentity>();
        foreach (var identity in final.OrderBy(identity => identity.Module, StringComparer.Ordinal)
                     .ThenBy(identity => identity.Name, StringComparer.Ordinal))
        {
            if (declaredWit.Contains(identity)) wit.Add(identity);
            else if (declaredJavaScript.Contains(identity)) javascript.Add(identity);
            else runtime.Add(identity);
        }
        return new(request.Catalog, wit.ToImmutable(), javascript.ToImmutable(), runtime.ToImmutable());
    }

    private static void ValidateIdentity(RawCanonicalImportIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Module);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.Name);
    }
}
