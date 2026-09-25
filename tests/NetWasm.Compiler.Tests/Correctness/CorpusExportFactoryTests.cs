namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusExportFactoryTests
{
    [Fact]
    public void CreateRequestsScalarTraceForOrdinaryFixtures()
    {
        var factory = Assert.IsAssignableFrom<ICorpusExportFactory>(new CorpusExportFactory());
        var exports = factory.Create(CreateFixture());

        Assert.Collection(
            exports,
            export => Assert.Equal(new("trace", CreateFixture().EntryType, "Trace"), export));
    }

    [Fact]
    public void CreateRequestsRunAndScalarTraceForReactorFixtures()
    {
        var factory = Assert.IsAssignableFrom<ICorpusExportFactory>(new CorpusExportFactory());
        var fixture = CreateFixture() with
        {
            RequiresReactor = true,
        };

        var exports = factory.Create(fixture);

        Assert.Collection(
            exports,
            export => Assert.Equal(new("run", fixture.EntryType, "Run"), export),
            export => Assert.Equal(new("trace", fixture.EntryType, "Trace"), export));
    }

    [Fact]
    public void CreateRequestsEveryTypedTraceExport()
    {
        var factory = Assert.IsAssignableFrom<ICorpusExportFactory>(new CorpusExportFactory());
        var fixture = CreateFixture() with
        {
            UsesTypedTrace = true,
        };

        var exports = factory.Create(fixture);

        Assert.Equal(
            ["trace", "trace_count", "trace_kind", "trace_event_id", "trace_payload_low", "trace_payload_high"],
            exports.Select(export => export.Name));
    }

    [Fact]
    public void CreateRequestsConfiguredReactorLifecycleExports()
    {
        var factory = Assert.IsAssignableFrom<ICorpusExportFactory>(new CorpusExportFactory());
        var fixture = CreateFixture() with
        {
            RequiresReactor = true,
            ExposesLegacyTrace = false,
            WasmEntryMethod = "Start",
            ReactorObserveMethod = "Observe",
        };

        var exports = factory.Create(fixture);

        Assert.Collection(
            exports,
            export => Assert.Equal(new("run", fixture.EntryType, "Start"), export),
            export => Assert.Equal(new("observe", fixture.EntryType, "Observe"), export));
    }

    [Fact]
    public void CreateAllowsFixturesWithoutLegacyTraceBoilerplate()
    {
        var factory = Assert.IsAssignableFrom<ICorpusExportFactory>(
            new CorpusExportFactory());
        var fixture = CreateFixture() with
        {
            ExposesLegacyTrace = false,
        };

        Assert.Empty(factory.Create(fixture));
    }

    private static CorpusFixture CreateFixture() =>
        new("CorpusExportFactoryUnit", "NetWasm.Correctness.Generated", "", [0]);
}
