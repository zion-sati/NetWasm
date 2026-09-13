using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Metadata;

public interface IReferenceClosureValidator
{
    void Validate(
        IEnumerable<AssemblyReferenceClosure> assemblies,
        IEnumerable<string> availableNames,
        ImmutableDictionary<string, string> aliases);
}
