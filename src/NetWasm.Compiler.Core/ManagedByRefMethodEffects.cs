using System.Collections.Generic;
using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

public sealed record ManagedByRefMethodEffects(
    ImmutableHashSet<int> WrittenArguments,
    ImmutableHashSet<int> EscapedArguments,
    ImmutableHashSet<int> ReturnedArgumentAliases)
{
    public static ManagedByRefMethodEffects Empty { get; } = new([], [], []);

    public bool RequiresOwnedStorage(int argumentIndex) =>
        WrittenArguments.Contains(argumentIndex) ||
        EscapedArguments.Contains(argumentIndex) ||
        ReturnedArgumentAliases.Contains(argumentIndex);

    public ManagedByRefMethodEffects Union(ManagedByRefMethodEffects other) => new(
        WrittenArguments.Union(other.WrittenArguments),
        EscapedArguments.Union(other.EscapedArguments),
        ReturnedArgumentAliases.Union(other.ReturnedArgumentAliases));
}

// Read through IReadOnlyDictionary so a compiled unit can record the methods it looks up.
public sealed record ManagedByRefEffects(
    IReadOnlyDictionary<string, ManagedByRefMethodEffects> Methods)
{
    public static ManagedByRefEffects Empty { get; } = new(
        ImmutableDictionary<string, ManagedByRefMethodEffects>.Empty);

    // The managed methods each analysed method (one with argument addresses) calls, whose
    // effects its own were derived from.
    public IReadOnlyDictionary<string, ImmutableArray<string>> Callees { get; init; } =
        ImmutableDictionary<string, ImmutableArray<string>>.Empty;
}
