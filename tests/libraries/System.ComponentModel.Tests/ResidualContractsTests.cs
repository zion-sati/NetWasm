using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.ComponentModel.Tests;

// Converted from the compiler harness's ResidualContractsCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class ResidualContractsTests
{
    [Test]
    public async Task Utf7CommandsAuthenticationAndResidualContractsMatchDesktop() => await Assert.That(ResidualContractsFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
