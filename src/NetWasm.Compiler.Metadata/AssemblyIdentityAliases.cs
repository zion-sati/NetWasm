using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal readonly record struct AssemblyIdentityAliases
{
    private readonly ImmutableDictionary<string, string>? _aliases;
    private readonly ImmutableDictionary<(AssemblyIdentity Assembly, string FullName), AssemblyIdentity>? _typeForwarders;

    public AssemblyIdentityAliases(ImmutableDictionary<string, string> aliases,
        ImmutableDictionary<(AssemblyIdentity Assembly, string FullName), AssemblyIdentity>? typeForwarders = null)
    {
        _aliases = aliases ?? throw new ArgumentNullException(nameof(aliases));
        _typeForwarders = typeForwarders;
    }

    public static AssemblyIdentityAliases Empty { get; } =
        new(ImmutableDictionary<string, string>.Empty);

    public bool HasTypeForwarders => _typeForwarders is { Count: > 0 };

    public AssemblyIdentity Canonicalize(AssemblyIdentity identity) =>
        _aliases?.TryGetValue(identity.Name, out var target) == true
            ? new AssemblyIdentity(target)
            : identity;

    public AssemblyIdentity Canonicalize(AssemblyIdentity identity, string fullName) =>
        _typeForwarders?.TryGetValue((identity, fullName), out var target) == true
            ? Canonicalize(target)
            : Canonicalize(identity);
}
