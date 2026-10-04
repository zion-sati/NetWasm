using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;

namespace NetWasm.Compiler.Browser;

/// <summary>
/// An immutable snapshot of compiler options and exact, ordinal virtual input paths.
/// Normalized WIT documents must have their original binary or source bytes in Inputs.
/// </summary>
public sealed record BrowserCompilationRequest
{
    public BrowserCompilationRequest(
        CompilerOptions options,
        IReadOnlyDictionary<string, byte[]> inputs,
        IReadOnlyDictionary<string, string> normalizedWitDocuments,
        bool selectManagedExecutableEntryPoint = false)
        : this(
            options,
            inputs,
            normalizedWitDocuments,
            ImmutableDictionary<string, string>.Empty,
            selectManagedExecutableEntryPoint)
    {
    }

    public BrowserCompilationRequest(
        CompilerOptions options,
        IReadOnlyDictionary<string, byte[]> inputs,
        IReadOnlyDictionary<string, string> normalizedWitDocuments,
        IReadOnlyDictionary<string, string> witCoreBindingInventories,
        bool selectManagedExecutableEntryPoint = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(normalizedWitDocuments);
        ArgumentNullException.ThrowIfNull(witCoreBindingInventories);
        if (options.DiagnosticTracePath is not null || options.DiagnosticLogPath is not null)
        {
            throw new ArgumentException(
                "Browser compilation does not support filesystem diagnostic output paths.",
                nameof(options));
        }

        if (selectManagedExecutableEntryPoint &&
            options.EntryPointKind != CompilerEntryPointKind.ManagedExecutable)
        {
            throw new ArgumentException(
                "PE entry-point selection requires a managed executable compilation.",
                nameof(options));
        }

        Options = options;
        SelectManagedExecutableEntryPoint = selectManagedExecutableEntryPoint;
        var images = ImmutableDictionary.CreateBuilder<string, ImmutableArray<byte>>(StringComparer.Ordinal);
        foreach (var (path, bytes) in inputs)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(bytes);
            images.Add(path, [.. bytes]);
        }

        Inputs = images.ToImmutable();
        var documents = ImmutableDictionary.CreateBuilder<string, string>(StringComparer.Ordinal);
        foreach (var (path, json) in normalizedWitDocuments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(json);
            if (!Inputs.ContainsKey(path))
            {
                throw new FileNotFoundException(
                    "The original bytes for a normalized virtual WIT document were not supplied.",
                    path);
            }

            documents.Add(path, json);
        }

        NormalizedWitDocuments = documents.ToImmutable();
        var inventories = ImmutableDictionary.CreateBuilder<string, string>(
            StringComparer.Ordinal);
        foreach (var (path, inventory) in witCoreBindingInventories)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentException.ThrowIfNullOrWhiteSpace(inventory);
            if (!NormalizedWitDocuments.ContainsKey(path))
            {
                throw new FileNotFoundException(
                    "A normalized virtual WIT document was not supplied for its core binding inventory.",
                    path);
            }
            inventories.Add(path, inventory);
        }
        WitCoreBindingInventories = inventories.ToImmutable();
    }

    public CompilerOptions Options { get; }

    /// <summary>
    /// Select the executable's original managed Main, including async Main behind
    /// a synchronous PE wrapper. False preserves the explicit compiler options.
    /// </summary>
    public bool SelectManagedExecutableEntryPoint { get; }

    public bool CollectCompilerMetrics { get; init; }

    public ImmutableDictionary<string, ImmutableArray<byte>> Inputs { get; }

    public ImmutableDictionary<string, string> NormalizedWitDocuments { get; }

    public ImmutableDictionary<string, string> WitCoreBindingInventories { get; }
}
