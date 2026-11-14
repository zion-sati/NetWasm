using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's TypeByRefLikeContractCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class TypeByRefLikeContractTests
{
    [Test]
    public async Task ByRefLikeFactsMatchDesktop() => await Assert.That(TypeByRefLikeFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
