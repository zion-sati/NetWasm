using System.Collections.Generic;

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// A type's own relationship key, and the keys of every type it can be assignment
/// compatible with: its own, its base types', its interfaces', object's for a managed
/// reference, and for an array the array key, System.Array's and the array interfaces'.
/// Named types and generic instantiations key by their definition, so variant
/// instantiations share a key. A target whose key is not among a candidate's keys is
/// never assignable from it. An unbounded type's keys are unknown, so every pair with
/// it is classified.
/// </summary>
internal sealed record TypeRelationshipKeys(
    string Own,
    IReadOnlySet<string> Reachable,
    bool Unbounded)
{
    // For each generic definition the type reaches through one of its instantiations, the keys an assignable
    // instantiation's first type argument can have; a null set allows any. An array's entry under the array key
    // admits the element keys of the array types it converts to. An invariant parameter, or a value type
    // argument, admits only the argument's own key; a covariant reference argument admits the keys of its own
    // hierarchy; a contravariant one admits any. Null when no argument facts were resolved, which admits any.
    public IReadOnlyDictionary<string, IReadOnlySet<string>?>? Arguments { get; init; }

    // A generic instantiation's first type argument's own key, or an array's element's, by which it is filed as a
    // target; null otherwise.
    public string? ArgumentKey { get; init; }
}
