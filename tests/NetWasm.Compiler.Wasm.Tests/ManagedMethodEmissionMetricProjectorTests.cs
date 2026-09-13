using System.Collections.Immutable;
using NetWasm.Compiler.Wasm.Emission.Methods;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class ManagedMethodEmissionMetricProjectorTests
{
    [Fact]
    public void ProjectsEveryMetricFieldAndPreservesOrder()
    {
        var first = Emission("first", [1, 2], 3, 4, 5,
            ImmutableDictionary<int, int>.Empty.Add(6, 7));
        var second = Emission("second", [8], 9, 10, 11,
            ImmutableDictionary<int, int>.Empty);

        var metrics = CreateProjector().Project([first, second]);

        Assert.Equal(["first", "second"], metrics.Select(metric => metric.Identity));
        Assert.Equal(3, metrics[0].WasmInstructionCount);
        Assert.Equal(2, metrics[0].WasmBodyBytes);
        Assert.Equal(4, metrics[0].CompileDurationTicks);
        Assert.Equal(5, metrics[0].PeakObservedManagedMemoryBytes);
        Assert.Equal(7, metrics[0].OriginalBlockEmissionCounts[6]);
    }

    [Fact]
    public void ProjectsEmptyInputAndRejectsMissingInput()
    {
        Assert.Empty(CreateProjector().Project([]));
        Assert.Throws<ArgumentNullException>(() => CreateProjector().Project(null!));
    }

    private static IManagedMethodEmissionMetricProjector CreateProjector() => new[]
    {
        new ManagedMethodEmissionMetricProjector(),
    }.Cast<IManagedMethodEmissionMetricProjector>().Single();

    private static ManagedMethodEmissionRecord Emission(string identity, byte[] body,
        int instructionCount, long duration, long peak,
        ImmutableDictionary<int, int> blockCounts) => new(identity, "key",
            new(body, instructionCount, duration, peak, "key",
                FilterEnvironmentLayout.Empty, blockCounts));
}
