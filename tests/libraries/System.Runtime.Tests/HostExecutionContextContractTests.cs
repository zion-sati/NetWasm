using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's HostExecutionContextContractCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class HostExecutionContextContractTests
{
    [Test]
    public async Task HostContextCopyDisposalAndRevertWork() => await Assert.That(HostContextFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
