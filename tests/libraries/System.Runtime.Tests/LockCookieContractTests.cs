using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's LockCookieContractCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class LockCookieContractTests
{
    [Test]
    public async Task DefaultCookieEqualityAndBoxingWork() => await Assert.That(LockCookieFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
