using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class CfgPropertyCorpusGenerator : ICfgPropertyCorpusGenerator
{
    public ImmutableArray<CfgPropertyCase> Generate(ImmutableArray<int> seeds)
    {
        if (seeds.IsDefaultOrEmpty || seeds.Length > 128)
        {
            throw new ArgumentException(
                "CFG property generation requires 1..128 recorded seeds",
                nameof(seeds));
        }
        return [.. seeds.Distinct().Order().SelectMany(seed =>
            (CfgPropertyCase[])
        [
            Create(seed, irreducible: false),
            Create(seed, irreducible: true),
        ])];
    }

    private static CfgPropertyCase Create(int seed, bool irreducible)
    {
        var shape = irreducible ? "Irreducible" : "Reducible";
        var name = $"Cfg{shape}{unchecked((uint)seed):X8}";
        var body = irreducible ? IrreducibleBody(seed) : ReducibleBody(seed);
        var source = $$"""
            namespace NetWasm.Correctness.Cfg.{{name}};

            public static class EntryPoint
            {
                private static int _trace;

                public static int Run(int input)
                {
                    var value = unchecked(input + {{seed & 15}});
                    var steps = 0;
                    {{body}}
                Exit:
                    _trace = unchecked(value * 31 + steps);
                    return value;
                }

                public static int Trace() => _trace;
            }
            """;
        var inputs = ImmutableArray.Create(-3, 0, 1, 4);
        var expected = inputs.ToImmutableDictionary(
            input => input,
            input => Interpret(seed, irreducible, input));
        var fixture = new CorpusFixture(
            name,
            $"NetWasm.Correctness.Cfg.{name}",
            source,
            inputs);
        return new(name, seed, shape, fixture, expected);
    }

    private static string ReducibleBody(int seed) => $$"""
        Loop:
            if (steps >= 5)
            {
                goto Exit;
            }
            steps++;
            if (((value + steps + {{seed & 7}}) & 1) == 0)
            {
                goto Even;
            }
            value = unchecked(value * 3 + 1);
            goto Loop;
        Even:
            value = unchecked(value + steps * 5);
            goto Loop;
        """;

    private static string IrreducibleBody(int seed) => $$"""
        if (((input + {{seed & 3}}) & 1) == 0)
        {
            goto Left;
        }
        goto Right;
        Left:
            value = unchecked(value + steps + 3);
            steps++;
            if (steps >= 5)
            {
                goto Exit;
            }
            goto Right;
        Right:
            value = unchecked(value ^ (steps + {{(seed & 7) + 5}}));
            steps++;
            if (steps >= 5)
            {
                goto Exit;
            }
            if ((value & 1) == 0)
            {
                goto Left;
            }
            goto Right;
        """;

    private static OracleObservation Interpret(
        int seed,
        bool irreducible,
        int input)
    {
        var value = unchecked(input + (seed & 15));
        var steps = 0;
        if (!irreducible)
        {
            while (steps < 5)
            {
                steps++;
                if (((value + steps + (seed & 7)) & 1) == 0)
                {
                    value = unchecked(value + steps * 5);
                }
                else
                {
                    value = unchecked(value * 3 + 1);
                }
            }
        }
        else
        {
            var left = ((input + (seed & 3)) & 1) == 0;
            while (steps < 5)
            {
                if (left)
                {
                    value = unchecked(value + steps + 3);
                    steps++;
                    left = false;
                }
                else
                {
                    value = unchecked(value ^ (steps + (seed & 7) + 5));
                    steps++;
                    left = steps < 5 && (value & 1) == 0;
                }
            }
        }
        return new(
            OracleObservationKind.Value,
            value,
            null,
            unchecked(value * 31 + steps));
    }
}
