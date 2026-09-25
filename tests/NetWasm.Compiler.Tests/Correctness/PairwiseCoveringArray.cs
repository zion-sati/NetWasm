using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class PairwiseCoveringArray : IPairwiseCoveringArray
{
    public ImmutableArray<ImmutableDictionary<string, string>> Generate(
        ImmutableArray<CorpusDimension> dimensions,
        int seed)
    {
        Validate(dimensions);
        var candidates = CartesianProduct(dimensions);
        var uncovered = CreateRequiredPairs(dimensions);
        var selected = ImmutableArray.CreateBuilder<ImmutableDictionary<string, string>>();

        while (uncovered.Count > 0)
        {
            var candidate = candidates
                .Select(values => new
                {
                    Values = values,
                    Score = CoveredPairs(values, dimensions, uncovered).Count,
                    TieBreaker = StableHash(values, dimensions, seed),
                })
                .Where(item => item.Score > 0)
                .OrderByDescending(item => item.Score)
                .ThenBy(item => item.TieBreaker)
                .ThenBy(item => Describe(item.Values, dimensions), StringComparer.Ordinal)
                .FirstOrDefault()
                ?? throw new InvalidOperationException("pairwise generation made no progress");
            selected.Add(candidate.Values);
            foreach (var pair in CoveredPairs(candidate.Values, dimensions, uncovered))
            {
                uncovered.Remove(pair);
            }
        }

        return selected.ToImmutable();
    }

    private static void Validate(ImmutableArray<CorpusDimension> dimensions)
    {
        if (dimensions.Length < 2)
        {
            throw new ArgumentException("at least two dimensions are required", nameof(dimensions));
        }
        if (dimensions.Any(dimension =>
                string.IsNullOrWhiteSpace(dimension.Name) || dimension.Levels.IsDefaultOrEmpty))
        {
            throw new ArgumentException(
                "every dimension requires a name and at least one level", nameof(dimensions));
        }
        if (dimensions.Select(dimension => dimension.Name)
            .Distinct(StringComparer.Ordinal).Count() != dimensions.Length)
        {
            throw new ArgumentException("dimension names must be unique", nameof(dimensions));
        }
        if (dimensions.Any(dimension => dimension.Levels
                .Distinct(StringComparer.Ordinal).Count() != dimension.Levels.Length))
        {
            throw new ArgumentException("dimension levels must be unique", nameof(dimensions));
        }
    }

    private static ImmutableArray<ImmutableDictionary<string, string>> CartesianProduct(
        ImmutableArray<CorpusDimension> dimensions)
    {
        var results = ImmutableArray.CreateBuilder<ImmutableDictionary<string, string>>();
        AddDimension(0, ImmutableDictionary<string, string>.Empty);
        return results.ToImmutable();

        void AddDimension(int index, ImmutableDictionary<string, string> values)
        {
            if (index == dimensions.Length)
            {
                results.Add(values);
                return;
            }
            var dimension = dimensions[index];
            foreach (var level in dimension.Levels)
            {
                AddDimension(index + 1, values.Add(dimension.Name, level));
            }
        }
    }

    private static HashSet<PairKey> CreateRequiredPairs(
        ImmutableArray<CorpusDimension> dimensions)
    {
        var pairs = new HashSet<PairKey>();
        for (var left = 0; left < dimensions.Length; left++)
        {
            for (var right = left + 1; right < dimensions.Length; right++)
            {
                foreach (var leftLevel in dimensions[left].Levels)
                {
                    foreach (var rightLevel in dimensions[right].Levels)
                    {
                        pairs.Add(new(left, leftLevel, right, rightLevel));
                    }
                }
            }
        }
        return pairs;
    }

    private static List<PairKey> CoveredPairs(
        ImmutableDictionary<string, string> values,
        ImmutableArray<CorpusDimension> dimensions,
        HashSet<PairKey> uncovered)
    {
        var pairs = new List<PairKey>();
        for (var left = 0; left < dimensions.Length; left++)
        {
            for (var right = left + 1; right < dimensions.Length; right++)
            {
                var pair = new PairKey(
                    left,
                    values[dimensions[left].Name],
                    right,
                    values[dimensions[right].Name]);
                if (uncovered.Contains(pair))
                {
                    pairs.Add(pair);
                }
            }
        }
        return pairs;
    }

    private static uint StableHash(
        ImmutableDictionary<string, string> values,
        ImmutableArray<CorpusDimension> dimensions,
        int seed)
    {
        var hash = unchecked((uint)seed) ^ 2166136261u;
        foreach (var character in Describe(values, dimensions))
        {
            hash = unchecked((hash ^ character) * 16777619u);
        }
        return hash;
    }

    private static string Describe(
        ImmutableDictionary<string, string> values,
        ImmutableArray<CorpusDimension> dimensions) => string.Join(
        '|', dimensions.Select(dimension => values[dimension.Name]));

    private sealed record PairKey(
        int LeftDimension,
        string LeftLevel,
        int RightDimension,
        string RightLevel);
}
