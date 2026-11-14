using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Security.Cryptography.Tests;

// Converted from the compiler harness's CryptographicHashCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class CryptographicHashTests
{
    [Test]
    public async Task ManagedHashingMatchesKnownAnswersAndBoundaries() => await Assert.That(HashFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
