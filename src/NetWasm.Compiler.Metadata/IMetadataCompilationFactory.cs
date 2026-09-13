using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata;

public interface IMetadataCompilationFactory
{
    IMetadataCompilationLease Create(
        ManagedAssembly entry,
        ImmutableDictionary<string, ManagedAssembly> assemblies,
        ImmutableDictionary<string, string> referenceAssemblyAliases);
}
