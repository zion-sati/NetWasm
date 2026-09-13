using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataCompilationLease : IMetadataCompilationLease
{
    private readonly IMetadataCompilationDisposer _disposer;
    private readonly MetadataLifetime _lifetime;
    private readonly MetadataCompilationSnapshot _snapshot;

    public MetadataCompilationSnapshot Snapshot
    {
        get
        {
            ObjectDisposedException.ThrowIf(_lifetime.IsDisposed, this);
            return _snapshot;
        }
    }

    internal MetadataCompilationLease(
        ManagedAssembly entryAssembly,
        ImmutableArray<ManagedAssembly> assemblies,
        ImmutableDictionary<string, string> referenceAssemblyAliases,
        IMetadataCompilationDisposer disposer,
        MetadataLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(entryAssembly);
        ArgumentNullException.ThrowIfNull(referenceAssemblyAliases);
        ArgumentNullException.ThrowIfNull(disposer);
        ArgumentNullException.ThrowIfNull(lifetime);

        _disposer = disposer;
        _lifetime = lifetime;

        _snapshot = new(
            assemblies,
            entryAssembly.Identity,
            [.. assemblies.SelectMany(assembly => assembly.Types.Values)],
            [.. assemblies.SelectMany(assembly => assembly.Methods.Values)],
            [.. entryAssembly.Methods.Values],
            referenceAssemblyAliases);
    }

    public void Dispose() => _disposer.Dispose();
}
