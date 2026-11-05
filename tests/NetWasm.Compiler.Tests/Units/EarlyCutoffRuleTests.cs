using System.Collections.Frozen;
using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.Inputs;
using NetWasm.Compiler.Pipeline;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Units;

namespace NetWasm.Compiler.Tests.Units;

// Managed optimizations analyse each method alone at O0 or none, without overrides other than
// the defaults: only then can an edited method's facts be derived from it alone.
public sealed class MethodLocalAnalysisTests
{
    private static readonly CompilerOptions Base = new("app.dll", [], "EntryPoint", "Run", []);

    public static TheoryData<string, bool> Settings => new()
    {
        { "O0", true }, { "none", true }, { "O2", false }, { "default overrides", true }, { "overrides", false },
    };

    [Theory]
    [MemberData(nameof(Settings))]
    public void MethodsAreAnalysedAloneOnlyWithoutCrossMethodOptimizations(string setting, bool alone) =>
        Assert.Equal(alone, CompilationInputDetector.AnalysesMethodsLocally(setting switch
        {
            "O0" => Base with { Optimization = FinalWasmOptimization.O0 },
            "none" => Base with { Optimization = FinalWasmOptimization.None },
            "O2" => Base with { Optimization = FinalWasmOptimization.O2 },
            "default overrides" => Base with
            {
                Optimization = FinalWasmOptimization.O0,
                OptimizationOverrides = new NetWasm.Compiler.ComponentModel.Optimization.WasmOptimizationOverrides(),
            },
            _ => Base with
            {
                Optimization = FinalWasmOptimization.O0,
                OptimizationOverrides = new NetWasm.Compiler.ComponentModel.Optimization.WasmOptimizationOverrides(
                    NetWasm.Compiler.ComponentModel.Optimization.ManagedInliningHints.Preserve),
            },
        }));
}

// Every analysed instance of an edited body must add what it added last time: the record of
// a compile, its account and the early cutoff all compare them alike.
public sealed class EditedContributionsTests
{
    private static MethodContributionRecord Record(int token, ulong fingerprint) => new("App", token, new(fingerprint, fingerprint));

    [Fact]
    public void TheEditedBodiesAreEachAssemblysChangedBodies()
    {
        static CompilationInputs Inputs(ulong first, ulong second) => new([
            new("App", new(first, second), new(new(1, 1), new Dictionary<int, InputFingerprint>
            {
                [1] = new(first, first),
                [2] = new(second, second),
            }.ToFrozenDictionary())),
        ]);

        Assert.Equal([("App", 2)], EditedContributions.Bodies(CompilationInputChanges.Between(Inputs(1, 1), Inputs(1, 2))));
    }

    [Fact]
    public void EachInstanceIsKeptChangedOrGone()
    {
        HashSet<(string, int)> edited = [("App", 1), ("App", 2)];
        var previous = new Dictionary<string, MethodContributionRecord>
        {
            ["Kept"] = Record(1, 5),
            ["Changed"] = Record(1, 6),
            ["Gone"] = Record(2, 7),
            ["Untouched"] = Record(3, 8),
        };
        var current = new Dictionary<string, MethodContributionRecord>
        {
            ["Kept"] = Record(1, 5),
            ["Changed"] = Record(1, 9),
            ["New"] = Record(2, 7),
            ["Untouched"] = Record(3, 0),
        };

        var compared = EditedContributions.Compare(edited, previous, current);

        Assert.Equal(1, compared.Kept);
        Assert.Equal(["Changed", "New"], compared.Changed.Order(StringComparer.Ordinal));
        Assert.Equal(["Gone"], compared.Gone);
    }

    [Fact]
    public void AChangedInstanceIsTheObstacleBeforeOneThatWentAway()
    {
        Assert.Null(new EditedContributions(2, [], []).Obstacle);
        Assert.Equal("M changed what it adds to the program", new EditedContributions(0, ["M"], ["N"]).Obstacle);
        Assert.Equal("N is no longer analysed", new EditedContributions(0, [], ["N"]).Obstacle);
    }
}

// A by-ref analysis that reads the edited method's own effects back through calls cannot be
// replayed alone.
public sealed class ByRefCallGraphTests
{
    private static readonly Dictionary<string, ImmutableArray<string>> Callees = new()
    {
        ["A"] = ["B"],
        ["B"] = ["C", "A"],
        ["C"] = ["M"],
        ["D"] = ["D"],
    };

    [Theory]
    [InlineData("", false)]
    [InlineData("M", true)]
    [InlineData("A", true)]
    [InlineData("D", false)]
    [InlineData("E", false)]
    public void ACallReachesTheMethodThroughAnyNumberOfCalls(string from, bool reaches) =>
        Assert.Equal(reaches, ByRefCallGraph.Reaches(from.Length == 0 ? [] : [from], "M", Callees));
}

// The stack-trace symbols are the last compile's when the current sources number them alike;
// an edited method's own locations may move within its body.
public sealed class EarlyCutoffStackTracesTests
{
    private static readonly AssemblyIdentity App = new("App");
    private static readonly EntityKey A = new(App, 0x06000001);
    private static readonly EntityKey B = new(App, 0x06000002);
    private static readonly EntityKey C = new(App, 0x06000003);
    private static readonly EntityKey Generic = new(App, 0x06000004);

    private static ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>> Sources(int offset, int line) =>
        ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>>.Empty.Add(A, [new(offset, "a.cs", line), new(8, null, 0)]);

    // The numbering the last compile kept, from its sources.
    private static UnitStackTraces Numbering(ImmutableDictionary<EntityKey, ImmutableArray<WasmSourceLocation>> sources)
    {
        var table = StackTraceSymbolTable.Build([(A, "A"), (B, "B")], [new("G<int>", Generic)], sources);
        return new(new Dictionary<EntityKey, int> { [A] = 1, [B] = 2 }, new Dictionary<string, int> { ["G<int>"] = 3 },
            new Dictionary<string, EntityKey> { ["G<int>"] = Generic },
            StackTraceSymbolTable.Fingerprint(table.Symbols, table.LocationSymbols));
    }

    [Fact]
    public void AnEditedMethodsLocationsMayMoveWithinItsBody()
    {
        var replay = EarlyCutoffStackTraces.Replay(Numbering(Sources(0, 3)), new Methods(), new Names(), Sources(4, 3), [A, B, C]);

        Assert.NotNull(replay);
        Assert.Equal(["A", "B", "G<int>"], replay.Table.Symbols.Take(3).Select(symbol => symbol.Name));
        Assert.Equal([1, 2], replay.EditedLocations.Keys.Order());
        Assert.Equal([4, 8], replay.EditedLocations[1].Value.Select(location => location.Offset));
        Assert.False(replay.EditedLocations[2].Found);
    }

    [Fact]
    public void ALocationOnAnotherLineIsAnotherNumbering() =>
        Assert.Null(EarlyCutoffStackTraces.Replay(Numbering(Sources(0, 3)), new Methods(), new Names(), Sources(0, 4), [A]));

    // A last compile with no symbols kept no numbering.
    [Fact]
    public void WithoutTheLastCompilesNumberingThereIsNoReplay() =>
        Assert.Null(EarlyCutoffStackTraces.Replay(null, new Methods(), new Names(), Sources(0, 3), [A]));

    private sealed class Methods : IMethodRepository
    {
        public MethodDefinitionModel GetMethod(EntityKey key) =>
            new(key, new EntityKey(App, 0x02000001), key == A ? "A" : "B", true, MethodSignatureModel.Create(CliValueKind.Void), 0);
    }

    private sealed class Names : ISymbolFormatter
    {
        public string Format(EntityKey key) => throw new NotSupportedException();

        public string Format(MethodDefinitionModel method) => method.Name;
    }
}

// A last compile that cannot answer, or whose kept data is damaged, declines the cutoff; any
// other failure is the compile's.
public sealed class DecliningEarlyCutoffEmitterTests
{
    private static readonly EarlyCutoffRequest Request = new(null!, null!, null!, null!);

    [Fact]
    public void TheCutoffsEmissionIsPassedOn()
    {
        var emission = new EarlyCutoffEmission([1], 1, "early cutoff: patched 1 of the last compile's functions\n");
        var inner = new Throwing(null, emission);

        Assert.Same(emission, new DecliningEarlyCutoffEmitter(inner).Emit(Request));
        Assert.Same(Request, inner.Asked);
    }

    [Fact]
    public void AnAnswerTheLastCompileLacksDeclines() =>
        Assert.Equal("early cutoff: declined (the last compile does not answer: no layout)\n",
            new DecliningEarlyCutoffEmitter(new Throwing(new UnitReplayMissException("no layout"), null)).Emit(Request).Account);

    [Fact]
    public void DamagedKeptDataDeclines()
    {
        var declined = new DecliningEarlyCutoffEmitter(new Throwing(new InvalidDataException("A unit record is corrupt."), null))
            .Emit(Request);

        Assert.Null(declined.Module);
        Assert.Equal("early cutoff: declined (the kept compile is damaged: A unit record is corrupt.)\n", declined.Account);
    }

    [Fact]
    public void AnyOtherFailureIsTheCompiles()
    {
        var failure = new InvalidOperationException("broken");

        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            new DecliningEarlyCutoffEmitter(new Throwing(failure, null)).Emit(Request)));
        Assert.Throws<ArgumentNullException>(() => new DecliningEarlyCutoffEmitter(null!));
    }

    private sealed class Throwing(Exception? failure, EarlyCutoffEmission? emission) : IEarlyCutoffEmitter
    {
        public EarlyCutoffRequest? Asked { get; private set; }

        public EarlyCutoffEmission Emit(EarlyCutoffRequest request)
        {
            Asked = request;
            return failure is null ? emission! : throw failure;
        }
    }
}
