using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Security.Cryptography.Tests;

// Converted from the compiler harness's CryptographyX509CompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class CryptographyX509Tests
{
    [Test]
    public async Task CertificateDataExtensionsRequestsAndChainPoliciesMatchDesktop() => await Assert.That(CryptoX509Fixture.EntryPoint.Run(41)).IsEqualTo(42);
}
