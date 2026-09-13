using System.Collections.Immutable;

namespace NetWasm.TimeZones;

internal sealed class TimeZoneSelectionSelector : ITimeZoneSelectionSelector
{
    public ImmutableArray<TimeZoneDefinition> Select(
        TimeZoneCatalog catalog,
        IReadOnlyCollection<string> requestedZones,
        bool includeAll)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(requestedZones);
        if (includeAll && requestedZones.Count != 0)
        {
            throw new ArgumentException(
                "All-zone generation cannot be combined with selected zones.");
        }
        if (!includeAll && requestedZones.Count == 0)
        {
            throw new ArgumentException("At least one explicit IANA zone is required.");
        }
        var selectedCanonicals = new HashSet<string>(StringComparer.Ordinal);
        if (includeAll)
        {
            selectedCanonicals.UnionWith(catalog.Definitions.Keys);
        }
        else
        {
            foreach (var requested in requestedZones)
            {
                if (catalog.Definitions.ContainsKey(requested))
                {
                    selectedCanonicals.Add(requested);
                }
                else if (catalog.Aliases.TryGetValue(requested, out var canonical))
                {
                    selectedCanonicals.Add(canonical);
                }
                else
                {
                    throw new ArgumentException(
                        $"IANA timezone '{requested}' does not exist in the source dataset.");
                }
            }
        }
        var output = ImmutableArray.CreateBuilder<TimeZoneDefinition>();
        foreach (var canonical in selectedCanonicals.Order(StringComparer.Ordinal))
        {
            output.Add(catalog.Definitions[canonical]);
            foreach (var alias in catalog.Aliases
                .Where(pair => pair.Value == canonical)
                .OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                output.Add(catalog.Definitions[canonical] with { Name = alias.Key });
            }
        }
        output.Sort(static (left, right) =>
            StringComparer.Ordinal.Compare(left.Name, right.Name));
        return output.ToImmutable();
    }
}
