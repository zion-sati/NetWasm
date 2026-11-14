using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

// Whether a compile input defines a type itself. A member reference through a forwarded type is checked
// against the original definition only when the original assembly has one: a package's copy that a metadata
// view replaces does, while a partial facade (NetWasm.App's System.Threading, which forwards Interlocked to
// CoreLib) defines none of the types it forwards.
internal interface IRuntimeTypeDefinitions
{
    bool Defines(AssemblyIdentity assembly, string fullName);
}

internal sealed class RuntimeTypeDefinitions : IRuntimeTypeDefinitions
{
    private readonly IReadOnlyDictionary<string, MetadataAssemblySnapshot> _assemblies;
    private readonly ConcurrentDictionary<string, FrozenSet<string>> _names = new(StringComparer.Ordinal);

    public RuntimeTypeDefinitions(IReadOnlyDictionary<string, MetadataAssemblySnapshot> assemblies) =>
        _assemblies = assemblies ?? throw new ArgumentNullException(nameof(assemblies));

    public bool Defines(AssemblyIdentity assembly, string fullName)
    {
        ArgumentNullException.ThrowIfNull(fullName);
        return _assemblies.TryGetValue(assembly.Name, out var snapshot) &&
            _names.GetOrAdd(assembly.Name, _ => snapshot.Types.Values
                .Select(type => type.FullName)
                .ToFrozenSet(StringComparer.Ordinal)).Contains(fullName);
    }
}
