using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Browser;

/// <summary>
/// In-memory inputs for validating a linked raw core module and producing its
/// JavaScript binding adapter. The caller inspects the modules in its own host.
/// </summary>
public sealed record BrowserRawBindingRequest
{
    public BrowserRawBindingRequest(
        RawCompilerImportSource source,
        string witPath,
        string? world,
        string runtimeWitPath,
        string? runtimeWorld,
        WasmTarget target,
        IReadOnlyDictionary<string, string> normalizedWitDocuments,
        IReadOnlyDictionary<string, string> witCoreBindingInventories,
        ImmutableArray<RawCoreFunctionImportSignature> runtimeImports,
        ImmutableArray<RawCoreFunctionImportSignature> finalImports)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(witPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeWitPath);
        ArgumentNullException.ThrowIfNull(normalizedWitDocuments);
        ArgumentNullException.ThrowIfNull(witCoreBindingInventories);
        if (target is not (WasmTarget.Wasm32 or WasmTarget.Wasm64))
        {
            throw new ArgumentOutOfRangeException(nameof(target));
        }
        if (runtimeImports.IsDefault)
        {
            throw new ArgumentException(
                "Runtime module imports must be explicit.",
                nameof(runtimeImports));
        }
        if (finalImports.IsDefault)
        {
            throw new ArgumentException(
                "Final module imports must be explicit.",
                nameof(finalImports));
        }

        Source = source;
        WitPath = witPath;
        World = world;
        RuntimeWitPath = runtimeWitPath;
        RuntimeWorld = runtimeWorld;
        Target = target;
        NormalizedWitDocuments = Copy(normalizedWitDocuments);
        WitCoreBindingInventories = Copy(witCoreBindingInventories);
        RuntimeImports = runtimeImports;
        FinalImports = finalImports;
    }

    public RawCompilerImportSource Source { get; }

    public string WitPath { get; }

    public string? World { get; }

    public string RuntimeWitPath { get; }

    public string? RuntimeWorld { get; }

    public WasmTarget Target { get; }

    public ImmutableDictionary<string, string> NormalizedWitDocuments { get; }

    public ImmutableDictionary<string, string> WitCoreBindingInventories { get; }

    public ImmutableArray<RawCoreFunctionImportSignature> RuntimeImports { get; }

    public ImmutableArray<RawCoreFunctionImportSignature> FinalImports { get; }

    private static ImmutableDictionary<string, string> Copy(
        IReadOnlyDictionary<string, string> values)
    {
        var result = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.Ordinal);
        foreach (var (path, value) in values)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            result.Add(path, value);
        }
        return result.ToImmutable();
    }
}
