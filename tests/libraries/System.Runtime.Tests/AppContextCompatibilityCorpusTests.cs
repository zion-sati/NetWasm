using System.Threading.Tasks;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's AppContextCompatibilityCorpusTests.cs; the expected lines are desktop .NET 11's results for each input.
public sealed class AppContextCompatibilityCorpusTests
{
    [Test]
    public async Task MatchesDesktop() => await Corpus.Matches(NetWasm.Correctness.AppContextCompatibility.EntryPoint.Run,
        "0:143");
}
