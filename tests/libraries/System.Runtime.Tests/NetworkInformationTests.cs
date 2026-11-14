using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's NetworkInformationCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class NetworkInformationTests
{
    [Test]
    public async Task NetworkInformationValuesContractsAndHostBoundariesMatchUpstream() => await Assert.That(NetworkInformationFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
