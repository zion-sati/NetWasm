using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal readonly record struct AssemblyIdentityAliases
{
    private readonly ImmutableDictionary<string, string>? _aliases;

    public AssemblyIdentityAliases(ImmutableDictionary<string, string> aliases)
    {
        _aliases = aliases ?? throw new ArgumentNullException(nameof(aliases));
    }

    public static AssemblyIdentityAliases Empty { get; } =
        new(ImmutableDictionary<string, string>.Empty);

    public AssemblyIdentity Canonicalize(AssemblyIdentity identity) =>
        _aliases?.TryGetValue(identity.Name, out var target) == true
            ? new AssemblyIdentity(target)
            : identity;
}
