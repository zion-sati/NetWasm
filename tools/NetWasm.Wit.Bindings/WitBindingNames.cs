namespace NetWasm.Wit.Bindings;

// C# names for declarations the bindings place side by side, in one namespace
// or one class. Each declaration lists its candidate names from plainest to
// most qualified; one whose name collides with another's moves on to its next
// candidate, so a world without collisions keeps the names WIT gives it and
// every declaration ends distinct. Declarations that run out of candidates
// still colliding stop with a diagnostic naming them.
internal static class WitBindingNames
{
    public static Dictionary<TKey, string> Assign<TKey>(
        IReadOnlyList<(TKey Key, string[] Candidates)> declarations,
        string description)
        where TKey : notnull
    {
        var levels = declarations.ToDictionary(static declaration => declaration.Key, static _ => 0);
        while (true)
        {
            var colliding = declarations
                .GroupBy(declaration => declaration.Candidates[levels[declaration.Key]], StringComparer.Ordinal)
                .Where(static group => group.Count() > 1)
                .ToArray();
            if (colliding.Length == 0)
            {
                return declarations.ToDictionary(
                    static declaration => declaration.Key,
                    declaration => declaration.Candidates[levels[declaration.Key]]);
            }
            var advanced = false;
            foreach (var declaration in colliding.SelectMany(static group => group))
            {
                if (levels[declaration.Key] < declaration.Candidates.Length - 1)
                {
                    levels[declaration.Key]++;
                    advanced = true;
                }
            }
            if (!advanced)
            {
                throw WitBindingException.Invalid(
                    $"WIT {description} {string.Join(", ", colliding[0].Select(static declaration => $"'{declaration.Key}'"))} " +
                    $"have the same C# name '{colliding[0].Key}'");
            }
        }
    }

    // The name a world gives an interface: its own for one it names from a
    // package, or the world's for one it declares inline or names itself.
    public static string Short(WitWorldItem item, WitInterface definition) =>
        item.Name.Contains(':', StringComparison.Ordinal) ? definition.Name : item.Name;

    // What qualifies an interface's name: its package, without the version,
    // for one the world names from a package; the world, for one it declares
    // inline or names itself, since those share the world's package.
    public static string Qualifier(WitWorldItem item, WitInterface definition, WitWorld world)
    {
        if (!item.Name.Contains(':', StringComparison.Ordinal))
        {
            return world.Name;
        }
        var version = definition.Package.IndexOf('@', StringComparison.Ordinal);
        return (version < 0 ? definition.Package : definition.Package[..version]).Replace(':', '-');
    }
}
