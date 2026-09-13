using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataCompilationDisposer(
    ImmutableArray<ManagedAssembly> assemblies,
    MetadataLifetime lifetime) : IMetadataCompilationDisposer
{
    public void Dispose()
    {
        if (lifetime.IsDisposed)
        {
            return;
        }

        foreach (var assembly in assemblies)
        {
            assembly.Dispose();
        }

        lifetime.IsDisposed = true;
    }
}
