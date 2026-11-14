using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's TypeArrayRankContractCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class TypeArrayRankContractTests
{
    [Test]
    public async Task ArrayRanksMatchDesktop() => await Assert.That(TypeArrayRankFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
