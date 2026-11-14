using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

// An ILLink.Substitutions.xml method stub: where every condition holds (a feature switch with the given value, from the
// substitution's assembly, type and method elements), a Boolean method returns Result and a void method does nothing
// (its Result is false and unused).
public sealed record FeatureSwitchStub(ImmutableArray<(string Feature, bool Value)> Conditions, bool Result)
{
    public bool Equals(FeatureSwitchStub? other) =>
        other is not null && Result == other.Result && Conditions.SequenceEqual(other.Conditions);

    public override int GetHashCode() =>
        Conditions.Aggregate(Result.GetHashCode(), (hash, condition) => HashCode.Combine(hash, condition));

    // The stub's result where the folded switches meet every condition, or null.
    public bool? Select(ImmutableDictionary<string, bool> switches) =>
        Conditions.All(condition => switches.TryGetValue(condition.Feature, out var value) && value == condition.Value)
            ? Result
            : null;
}
