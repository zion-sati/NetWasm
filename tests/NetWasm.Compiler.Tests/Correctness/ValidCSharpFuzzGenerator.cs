using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class ValidCSharpFuzzGenerator : IValidCSharpFuzzGenerator
{
    private const int MaximumSeeds = 256;

    public ImmutableArray<GeneratedCorpusCase> Generate(ImmutableArray<int> seeds)
    {
        if (seeds.IsDefaultOrEmpty || seeds.Length > MaximumSeeds)
        {
            throw new ArgumentException(
                $"valid C# fuzzing requires 1..{MaximumSeeds} recorded seeds",
                nameof(seeds));
        }
        return [.. seeds.Distinct().Order().Select(Create)];
    }

    private static GeneratedCorpusCase Create(int seed)
    {
        var random = new DeterministicFuzzRandom(unchecked((uint)seed));
        var loop = random.Next(3);
        var branch = random.Next(3);
        var operation = random.Next(4);
        var cleanup = random.Next(2);
        var name = $"GrammarFuzz{unchecked((uint)seed):X8}";
        var body = Branch(branch, Operation(operation));
        var loopSource = Loop(loop, body);
        var guarded = cleanup == 0
            ? loopSource
            : $$"""
                try
                {
                    {{loopSource}}
                }
                finally
                {
                    _trace = unchecked(_trace * 31 + 17);
                }
                """;
        var source = $$"""
            namespace NetWasm.Correctness.Fuzz.{{name}};

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    _trace = {{seed}};
                    var value = unchecked(input * 3 + {{random.Next(17) + 1}});
                    var limit = (input & 3) + 1;
                    {{guarded}}
                    _trace = unchecked(_trace * 31 + value);
                    return value;
                }

                public static int Trace() => _trace;
            }
            """;
        var dimensions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["loop"] = loop.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["branch"] = branch.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["operation"] = operation.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            ["cleanup"] = cleanup.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
        }.ToImmutableDictionary(StringComparer.Ordinal);
        var fixture = new CorpusFixture(
            name,
            $"NetWasm.Correctness.Fuzz.{name}",
            source,
            [-3, 0, 1, 5]);
        return new(name, "GrammarFuzz", seed, dimensions, fixture);
    }

    private static string Loop(int shape, string body) => shape switch
    {
        0 => $$"""
            for (var index = 0; index < limit; index++)
            {
                {{body}}
            }
            """,
        1 => $$"""
            var index = 0;
            while (index < limit)
            {
                {{body}}
                index++;
            }
            """,
        _ => $$"""
            var index = 0;
            do
            {
                {{body}}
                index++;
            }
            while (index < limit);
            """,
    };

    private static string Branch(int shape, string operation) => shape switch
    {
        0 => $$"""
            if (((value + index) & 1) == 0)
            {
                {{operation}}
            }
            else
            {
                value = unchecked(value - index - 1);
            }
            """,
        1 => $$"""
            switch ((value ^ index) & 3)
            {
                case 0:
                    {{operation}}
                    break;
                case 1:
                    value = unchecked(value + 7);
                    break;
                default:
                    value = unchecked(value - 3);
                    break;
            }
            """,
        _ => $$"""
            value = ((value + index) & 1) == 0
                ? unchecked(value + index + 5)
                : unchecked(value - index - 2);
            {{operation}}
            """,
    };

    private static string Operation(int shape) => shape switch
    {
        0 => "value = unchecked(value * 3 + index);",
        1 => "value = unchecked(value ^ (index + 11));",
        2 => "value = unchecked((value << 1) - index);",
        _ => "value = unchecked(value + (index + 1) * 7);",
    };

    private sealed class DeterministicFuzzRandom(uint state)
    {
        private uint _state = state == 0 ? 0x9e3779b9u : state;

        public int Next(int exclusiveMaximum)
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return (int)(_state % (uint)exclusiveMaximum);
        }
    }
}
