using System.Threading.Tasks;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's AsciiCorpusTests.cs; the expected lines are desktop .NET 11's results for each input.
public sealed class AsciiCorpusTests
{
    [Test]
    public async Task MatchesDesktop() => await Corpus.Matches(NetWasm.Correctness.AsciiCorpus.EntryPoint.Run,
        "0:2135261328",
        "1:1892378545",
        "2:-1176845865",
        "3:1380999979",
        "4:2066811398");
}
