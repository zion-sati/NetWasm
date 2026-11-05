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
    bool Unbounded);
