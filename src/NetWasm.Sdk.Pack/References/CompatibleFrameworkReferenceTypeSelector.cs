using System.Collections.Frozen;

namespace NetWasm.Sdk.Pack.References;

/// <summary>Retains incompatible declarations instead of forwarding them to a different contract.</summary>
public sealed class CompatibleFrameworkReferenceTypeSelector(
    IFrameworkReferenceTypeSelector ownership,
    IFrameworkTypeContractChecker checker) : IFrameworkReferenceTypeSelector
{
    public IReadOnlyList<string> SelectTypes(FrameworkReferenceSelection request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var selected = ownership.SelectTypes(request).ToHashSet(StringComparer.Ordinal);
        var source = request.Implementation.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        var target = request.Platform.MainModule.GetTypes().ToDictionary(type => type.FullName, StringComparer.Ordinal);
        // Removing one identity can invalidate another type's base or constraint.
        // Each nonterminal iteration removes at least one candidate.
        while (selected.Count != 0)
        {
            var scope = new FrameworkContractScope(request.Implementation.Name, request.Platform.Name,
                selected.ToFrozenSet(StringComparer.Ordinal), target.Keys.ToFrozenSet(StringComparer.Ordinal));
            var rejected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var name in selected)
            {
                var type = source[name];
                if (type.DeclaringType is { } parent && !selected.Contains(parent.FullName) ||
                    type.NestedTypes.Any(nested => (nested.IsNestedPublic || nested.IsNestedFamily || nested.IsNestedFamilyOrAssembly) &&
                        !selected.Contains(nested.FullName)) ||
                    checker.Check(new(type, target[name], target, scope)) != FrameworkTypeMismatch.None)
                    rejected.Add(name);
            }
            if (rejected.Count == 0) break;
            selected.ExceptWith(rejected);
        }
        return selected.Order(StringComparer.Ordinal).ToArray();
    }
}
