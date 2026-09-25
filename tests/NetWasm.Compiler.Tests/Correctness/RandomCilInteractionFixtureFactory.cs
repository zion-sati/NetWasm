using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace NetWasm.Compiler.Tests.Correctness;

internal interface IRandomCilInteractionFixtureFactory
{
    ImmutableArray<int> BoundaryInputs { get; }

    CorpusFixture CreateTemplate(
        CilProfile profile,
        int slotCount = RandomCilInteractionShardPlanner.DefaultCasesPerShard);

    CorpusFixture CreateShard(
        CilProfile profile,
        RandomCilInteractionShard shard,
        int slotCount = RandomCilInteractionShardPlanner.DefaultCasesPerShard);
}

internal sealed class RandomCilInteractionFixtureFactory : IRandomCilInteractionFixtureFactory
{
    public ImmutableArray<int> BoundaryInputs { get; } =
        [int.MinValue, -17, -1, 0, 1, 19, int.MaxValue];

    public CorpusFixture CreateTemplate(
        CilProfile profile,
        int slotCount = RandomCilInteractionShardPlanner.DefaultCasesPerShard) =>
        Create(
            $"RandomCilInteractionTemplate{profile}",
            Enumerable.Range(0, slotCount * BoundaryInputs.Length).ToImmutableArray(),
            slotCount);

    public CorpusFixture CreateShard(
        CilProfile profile,
        RandomCilInteractionShard shard,
        int slotCount = RandomCilInteractionShardPlanner.DefaultCasesPerShard)
    {
        ArgumentNullException.ThrowIfNull(shard);
        var inputCount = checked(shard.Cases.Length * BoundaryInputs.Length);
        return Create(
            $"RandomCilInteraction{profile}Shard{shard.Index:D4}",
            Enumerable.Range(0, inputCount).ToImmutableArray(),
            slotCount);
    }

    private CorpusFixture Create(
        string name,
        ImmutableArray<int> inputs,
        int slotCount) =>
        new(
            name,
            "NetWasm.Correctness.RandomCil",
            CreateSource(slotCount),
            inputs)
        {
            OracleMode = OracleMode.SameIl,
            SameSourceReason = null,
            SupportsBatchedOracle = true,
            ExecuteWasm64 = true,
            ExecuteOptimizedWasm = true,
            AllowUnsafe = true,
        };

    private string CreateSource(int slotCount)
    {
        if (slotCount is <= 0 or > RandomCilInteractionShardPlanner.DefaultCasesPerShard)
        {
            throw new ArgumentOutOfRangeException(nameof(slotCount));
        }
        var source = RandomCilFixtureFactory.CreateSource().Replace(
            "public static int Run(int input)",
            "public static int BaselineRun(int input)",
            StringComparison.Ordinal);
        const string insertionMarker = "    public static int Trace";
        var insertionIndex = source.IndexOf(insertionMarker, StringComparison.Ordinal);
        if (insertionIndex < 0)
        {
            throw new InvalidOperationException("Random CIL template insertion marker is absent.");
        }

        var generated = new StringBuilder(source.Length + 256_000);
        generated.Append(source.AsSpan(0, insertionIndex));
        AppendDispatcher(generated, slotCount);
        for (var slot = 0; slot < slotCount; slot++)
        {
            AppendSlot(generated, slot);
        }

        generated.Append(source.AsSpan(insertionIndex));
        return generated.ToString();
    }

    private void AppendDispatcher(StringBuilder source, int slotCount)
    {
        source.Append("    public static int Run(int input) => input switch\n    {\n");
        for (var slot = 0; slot < slotCount; slot++)
        {
            for (var boundary = 0; boundary < BoundaryInputs.Length; boundary++)
            {
                var encoded = checked(slot * BoundaryInputs.Length + boundary);
                source.Append("        ")
                    .Append(encoded.ToString(CultureInfo.InvariantCulture))
                    .Append(" => Case")
                    .Append(slot.ToString("D3", CultureInfo.InvariantCulture))
                    .Append('(')
                    .Append(BoundaryInputs[boundary].ToString(CultureInfo.InvariantCulture))
                    .Append("),\n");
            }
        }

        source.Append("        _ => 0,\n    };\n");
    }

    private static void AppendSlot(StringBuilder source, int slot)
    {
        source.Append("    public static int Case")
            .Append(slot.ToString("D3", CultureInfo.InvariantCulture))
            .Append("(int input)\n    {\n")
            .Append("        var value = input;\n")
            .Append("        var box = new Box { Value = value };\n")
            .Append("        int loopBudget = value ^ input;\n")
            .Append("        try\n        {\n")
            .Append("            try\n            {\n");

        for (var index = 1; index <= 24; index++)
        {
            source.Append("                value = unchecked(value * 31 + input + ")
                .Append(index.ToString(CultureInfo.InvariantCulture))
                .Append(");\n");
        }

        source.Append("            }\n")
            .Append("            catch (System.Exception) when (input == int.MinValue)\n")
            .Append("            {\n                value = unchecked(value + 1);\n            }\n")
            .Append("        }\n        finally\n        {\n")
            .Append("            value = unchecked(value ^ input);\n")
            .Append("        }\n        return box.Value == int.MinValue ? box.Value : value + loopBudget;\n    }\n");
    }
}
