using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Wasm.Tests.Units;

public sealed class UnitDependencyLogTests
{
    private static readonly CliTypeIdentity Point = CliTypeIdentity.Named(
        new AssemblyIdentity("Program"), "", "Point", isValueType: false);
    private static readonly CliTypeIdentity Missing = CliTypeIdentity.Named(
        new AssemblyIdentity("Program"), "", "Missing", isValueType: false);

    [Fact]
    public void AMethodsQueriesAreRecordedOnceWithTheirAnswers()
    {
        var log = new UnitDependencyLog();
        var layouts = new RecordingTypeLayoutProvider(new FixedLayouts(), log);

        _ = layouts.StringTypeId;
        log.Begin();
        _ = layouts.GetObjectLayout(Point);
        _ = layouts.GetObjectLayout(Point);
        Assert.False(layouts.GetObjectLayout(Missing, out _));
        _ = layouts.GetObjectLayout(new EntityKey(new AssemblyIdentity("Program"), 0x02000002));
        _ = layouts.StringTypeId;
        var dependencies = log.End();

        Assert.Equal(
            [
                (UnitQueryKind.ObjectLayout, Point.CanonicalName),
                (UnitQueryKind.ObjectLayoutLookup, Missing.CanonicalName),
                (UnitQueryKind.ObjectLayoutOfDefinition, "Program:0x02000002"),
                (UnitQueryKind.StringTypeId, ""),
            ],
            dependencies.Select(dependency => (dependency.Kind, dependency.Key)).Order());
        Assert.Throws<InvalidOperationException>(() => log.End());
    }

    [Fact]
    public void OneQueryAnsweredTwoWaysWithinAMethodIsAnError()
    {
        var log = new UnitDependencyLog();
        log.Begin();
        log.Record(UnitQueryKind.StringTypeId, "", new(1, 1));

        Assert.Throws<InvalidOperationException>(() =>
            log.Record(UnitQueryKind.StringTypeId, "", new(2, 2)));
    }

    [Fact]
    public void StaticDataLookupsAreRecordedByValue()
    {
        var log = new UnitDependencyLog();
        var data = new RecordingStaticDataLayout(new FixedStaticData(), log);

        log.Begin();
        _ = data.GetStringLayout("hello");
        Assert.True(data.TypeObjectAddresses.TryGetValue("Program::Point", out var address));
        Assert.Equal(128, address);
        Assert.False(data.TypeObjectAddresses.TryGetValue("Program::Missing", out _));
        _ = data.DataSegments;
        _ = data.DataSegments;
        var dependencies = log.End();

        Assert.Equal(
            [
                (UnitQueryKind.StringLayout, "hello"),
                (UnitQueryKind.DataSegments, ""),
                (UnitQueryKind.TypeObjectAddress, "Program::Missing"),
                (UnitQueryKind.TypeObjectAddress, "Program::Point"),
            ],
            dependencies.Select(dependency => (dependency.Kind, dependency.Key)).Order());
    }

    [Fact]
    public void ALookupViewRecordsEachKeyLookedUpAndWhatItFound()
    {
        var log = new UnitDependencyLog();
        var effects = new Dictionary<string, ManagedByRefMethodEffects>
        {
            ["Callee"] = new([0], [], []),
        };
        var view = new RecordingLookup<string, ManagedByRefMethodEffects>(Lookups.Of(effects), log,
            UnitQueryKind.ByRefEffects, key => key, static (ref answer, value) => answer.Add(value));

        log.Begin();
        Assert.True(view.TryGetValue("Callee", out var callee));
        Assert.False(view.TryGetValue("Other", out _));
        var dependencies = log.End();

        Assert.Same(effects["Callee"], callee);
        Assert.Equal(["Callee", "Other"], dependencies.Select(dependency => dependency.Key).Order());
    }

    private sealed class FixedStaticData : IStaticDataLayout
    {
        public StringLayout GetStringLayout(string value) => new(64, value.Length, 12);
        public int StaticDataEnd => 256;
        public System.Collections.Immutable.ImmutableArray<int> StaticRootAddresses => [];
        public System.Collections.Immutable.ImmutableArray<DataSegment> DataSegments => [new(16, [1, 2])];
        public IEntryLookup<string, int> TypeObjectAddresses =>
            Lookups.Of(new Dictionary<string, int> { ["Program::Point"] = 128 });
    }

    private sealed class FixedLayouts : ITypeLayoutProvider
    {
        public int StringTypeId => 2;
        public int TypeTypeId => 3;
        public ObjectLayout GetObjectLayout(CliTypeIdentity type) => new(9, 16, []);
        public bool GetObjectLayout(CliTypeIdentity type, out ObjectLayout layout)
        {
            layout = default;
            return false;
        }
        public ObjectLayout GetObjectLayout(EntityKey type) => new(10, 24, [8]);
    }
}
