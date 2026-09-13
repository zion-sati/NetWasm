using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class ManagedAssemblyResolver(
    ImmutableDictionary<string, ManagedAssembly> assemblies,
    ImmutableDictionary<string, string> aliases,
    IMetadataAvailabilityValidator availability) : IManagedAssemblyResolver
{
    public ManagedAssembly Resolve(AssemblyIdentity identity)
    {
        availability.Validate();
        var name = aliases.TryGetValue(identity.Name, out var alias)
            ? alias
            : identity.Name;
        return assemblies.TryGetValue(name, out var assembly)
            ? assembly
            : throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.AssemblyResolution,
                    $"assembly '{identity.Name}' is not in the compilation unit"));
    }
}
