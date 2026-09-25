using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record RandomCilInteractionDimension(
    string Name,
    ImmutableArray<string> Levels);

internal sealed record RandomCilInteractionCase(
    string Id,
    int Seed,
    ImmutableDictionary<string, string> Levels);

internal interface IRandomCilInteractionMatrix
{
    ImmutableArray<RandomCilInteractionDimension> Dimensions { get; }

    ImmutableArray<int> Seeds { get; }

    ImmutableArray<RandomCilInteractionCase> GenerateExhaustive();
}

internal sealed class RandomCilInteractionMatrix : IRandomCilInteractionMatrix
{
    public ImmutableArray<RandomCilInteractionDimension> Dimensions { get; } =
    [
        new("numeric", ["arithmetic", "checked", "division", "bitwise", "conversion", "unsigned-finite"]),
        new("cfg", ["diamond", "switch", "overlap", "backedge"]),
        new("stack", ["empty-join", "value-join", "local-roundtrip", "duplicate-pop"]),
        new("eh", ["none", "finally", "nested-finally", "filter-finally", "fault-catch"]),
        new("data", ["argument-local", "array", "static-array-initializer", "range-slice", "object-field", "boxed-value"]),
        new("call", ["none", "direct", "virtual", "interface", "default-interface", "static-interface", "covariant-return", "indirect"]),
        new("lifetime", ["none", "reference-across-allocation"]),
        new("order", ["forward", "reverse-compatible", "interleaved"]),
    ];

    public ImmutableArray<int> Seeds { get; } = [0x51a7, 0x61d, 0x7f31];

    public ImmutableArray<RandomCilInteractionCase> GenerateExhaustive()
    {
        var cases = ImmutableArray.CreateBuilder<RandomCilInteractionCase>();
        AddDimension(0, ImmutableDictionary<string, string>.Empty);
        return cases.ToImmutable();

        void AddDimension(
            int index,
            ImmutableDictionary<string, string> selected)
        {
            if (index == Dimensions.Length)
            {
                foreach (var seed in Seeds)
                {
                    var description = string.Join(
                        '|',
                        Dimensions.Select(dimension =>
                            dimension.Name + '=' + selected[dimension.Name]));
                    cases.Add(new(
                        $"rit-{cases.Count:D6}-{StableHash(description, seed):x8}-{seed:x8}",
                        seed,
                        selected));
                }
                return;
            }

            var dimension = Dimensions[index];
            foreach (var level in dimension.Levels)
            {
                AddDimension(index + 1, selected.Add(dimension.Name, level));
            }
        }
    }

    private static uint StableHash(string description, int seed)
    {
        var hash = unchecked((uint)seed) ^ 2166136261u;
        foreach (var character in description)
        {
            hash = unchecked((hash ^ character) * 16777619u);
        }
        return hash;
    }
}
