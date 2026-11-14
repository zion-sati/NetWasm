using System.Threading.Tasks;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's BinaryPrimitivesCorpusTests.cs; the expected lines are desktop .NET 11's results for each input.
public sealed class BinaryPrimitivesCorpusTests
{
    [Test]
    public async Task MatchesDesktop() => await Corpus.Matches(NetWasm.Correctness.BinaryPrimitivesCorpus.EntryPoint.Run,
        "0:1929157560",
        "1:-315586009",
        "2:-432302363",
        "3:-198901510",
        "4:-1553508939",
        "5:936865113",
        "6:1487812727",
        "7:722183582");
}
