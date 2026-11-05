using System.Collections.Frozen;
using System.Collections.Immutable;
using NetWasm.Compiler.Metadata.Inputs;
using NetWasm.Compiler.Pipeline;

namespace NetWasm.Compiler.Tests.Units;

// The early cutoff applies only when bodies alone changed and every analysed instance of
// an edited body adds to the program what it added last time.
public sealed class EarlyCutoffVerdictTests
{
    private static readonly InputFingerprint Shape = new(1, 1);

    [Fact]
    public void ItNeedsAnEarlierCompile() =>
        Assert.Contains("no (no earlier compile)", Verdict(CompilationInputs.Empty, Inputs(10)));

    [Fact]
    public void AShapeChangeStopsIt()
    {
        var before = Inputs(10);
        var after = new CompilationInputs([new("App", new(9, 9), new(new(2, 2), Bodies(10)))]);
        Assert.Contains("changed shape", Verdict(before, after));
    }

    [Fact]
    public void OverriddenOptimizationsStopIt() =>
        Assert.Contains("managed optimizations analyse across methods", CompilationInputRecorder.DescribeCutoff(
            new("units", Inputs(10), Inputs(11), CompilationInputChanges.Between(Inputs(10), Inputs(11)))
            {
                MethodLocalAnalysis = false,
            },
            Inputs(11)));

    [Fact]
    public void AnUnchangedCompileQualifies() =>
        Assert.Contains("yes (nothing changed)", Verdict(Inputs(10), Inputs(10)));

    [Fact]
    public void AnEditThatKeepsItsContributionQualifies() =>
        Assert.Contains("yes (1 analysed instances of 1 edited bodies",
            Verdict(Inputs(10, contribution: 5), Inputs(11, contribution: 5)));

    [Fact]
    public void AnEditThatChangesItsContributionStopsIt() =>
        Assert.Contains("M changed what it adds",
            Verdict(Inputs(10, contribution: 5), Inputs(11, contribution: 6)));

    // An edited method the program no longer reaches adds nothing it added before.
    [Fact]
    public void AnEditedMethodNoLongerAnalysedStopsIt() =>
        Assert.Contains("M is no longer analysed",
            Verdict(Inputs(10, contribution: 5), Inputs(11)), StringComparison.Ordinal);

    [Fact]
    public void TheAccountNamesEachEditedInstanceThatChangedOrWentAway()
    {
        static MethodContributionRecord Record(int token, ulong fingerprint) => new("App", token, new(fingerprint, fingerprint));
        var before = new CompilationInputs([new("App", new(1, 1), new(Shape, Bodies(10)))])
        {
            Contributions = new Dictionary<string, MethodContributionRecord>
            {
                ["Kept"] = Record(0x06000001, 1),
                ["Changed"] = Record(0x06000001, 2),
                ["Gone"] = Record(0x06000001, 3),
            }.ToFrozenDictionary(),
        };
        var after = new CompilationInputs([new("App", new(2, 2), new(Shape, Bodies(11)))])
        {
            Contributions = new Dictionary<string, MethodContributionRecord>
            {
                ["Kept"] = Record(0x06000001, 1),
                ["Changed"] = Record(0x06000001, 9),
                ["New"] = Record(0x06000001, 4),
                ["Unedited"] = Record(0x06000002, 5),
            }.ToFrozenDictionary(),
        };

        var account = CompilationInputRecorder.DescribeContributions(before, after, CompilationInputChanges.Between(before, after));

        Assert.StartsWith("contributions: 1 kept, 3 changed: ", account, StringComparison.Ordinal);
        Assert.Contains("Gone (no longer analysed)", account, StringComparison.Ordinal);
        Assert.Equal("contributions: no edited methods to compare\n", CompilationInputRecorder.DescribeContributions(
            before, before, CompilationInputChanges.Between(before, before)));
    }

    private static string Verdict(CompilationInputs before, CompilationInputs after) =>
        CompilationInputRecorder.DescribeCutoff(
            new("units", before, after, CompilationInputChanges.Between(before, after)),
            after);

    // One assembly whose one body, token 0x06000001, has the given fingerprint.
    private static CompilationInputs Inputs(ulong body, ulong? contribution = null) =>
        new([new("App", new(body, body), new(Shape, Bodies(body)))])
        {
            Contributions = contribution is { } value
                ? new Dictionary<string, MethodContributionRecord>
                {
                    ["M"] = new("App", 0x06000001, new(value, value)),
                }.ToFrozenDictionary()
                : FrozenDictionary<string, MethodContributionRecord>.Empty,
        };

    private static FrozenDictionary<int, InputFingerprint> Bodies(ulong body) =>
        new Dictionary<int, InputFingerprint> { [0x06000001] = new(body, body) }.ToFrozenDictionary();
}
