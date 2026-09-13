using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Metadata;

public sealed class MetadataCompilationFactory : IMetadataCompilationFactory
{
    public IMetadataCompilationLease Create(
        ManagedAssembly entry,
        ImmutableDictionary<string, ManagedAssembly> assemblies,
        ImmutableDictionary<string, string> referenceAssemblyAliases)
    {
        var lifetime = new MetadataLifetime();
        var orderedAssemblies = assemblies.Values
            .OrderBy(assembly => assembly.Identity.Name, StringComparer.Ordinal)
            .ToImmutableArray();

        return new MetadataCompilationLease(
            entry,
            orderedAssemblies,
            referenceAssemblyAliases,
            new MetadataCompilationDisposer(orderedAssemblies, lifetime),
            lifetime);
    }
}
