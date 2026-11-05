using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Metadata.Inputs;

namespace NetWasm.Compiler.Pipeline;

/// <summary>
/// How the analysed instances of the edited methods compare with the last compile's: how
/// many add what they added before, which changed what they add, and which earlier instances
/// are no longer analysed. The early cutoff needs every instance kept.
/// </summary>
internal sealed record EditedContributions(int Kept, ImmutableArray<string> Changed, ImmutableArray<string> Gone)
{
    // The edited bodies, by assembly and token.
    public static HashSet<(string Assembly, int Token)> Bodies(CompilationInputChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return changes.Assemblies.SelectMany(assembly => assembly.ChangedBodies.Select(token => (assembly.Name, token))).ToHashSet();
    }

    public static EditedContributions Compare(IReadOnlySet<(string Assembly, int Token)> edited,
        IReadOnlyDictionary<string, MethodContributionRecord> previous,
        IReadOnlyDictionary<string, MethodContributionRecord> current)
    {
        ArgumentNullException.ThrowIfNull(edited);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(current);
        var kept = 0;
        var changed = ImmutableArray.CreateBuilder<string>();
        foreach (var (name, contribution) in current)
        {
            if (!edited.Contains((contribution.Assembly, contribution.Token))) continue;
            if (previous.TryGetValue(name, out var before) && before.Fingerprint == contribution.Fingerprint)
                kept++;
            else
                changed.Add(name);
        }
        var gone = ImmutableArray.CreateBuilder<string>();
        foreach (var (name, contribution) in previous)
        {
            if (edited.Contains((contribution.Assembly, contribution.Token)) && !current.ContainsKey(name))
                gone.Add(name);
        }
        return new(kept, changed.ToImmutable(), gone.ToImmutable());
    }

    // Why the early cutoff cannot apply, or null when every instance is kept.
    public string? Obstacle =>
        !Changed.IsEmpty ? $"{Changed[0]} changed what it adds to the program"
        : !Gone.IsEmpty ? $"{Gone[0]} is no longer analysed"
        : null;
}
