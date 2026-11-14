using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Security.Cryptography.Tests;

// Converted from the compiler harness's CryptographyRemainingCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class CryptographyRemainingTests
{
    [Test]
    public async Task SymmetricBasesParametersOidsAndTransformsMatchDesktop() => await Assert.That(CryptoRemainingFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
