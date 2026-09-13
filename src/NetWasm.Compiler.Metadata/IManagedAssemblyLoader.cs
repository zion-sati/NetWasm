using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata;

public interface IManagedAssemblyLoader
{
    ManagedAssembly Load(
        string path,
        ImmutableDictionary<string, string> referenceAssemblyAliases);
}
