using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata;

public interface IMetadataCompilationLoader
{
    IMetadataCompilationLease Load(
        string entryAssemblyPath,
        IEnumerable<string> referencePaths,
        ImmutableDictionary<string, string>? referenceAssemblyAliases = null);
}
