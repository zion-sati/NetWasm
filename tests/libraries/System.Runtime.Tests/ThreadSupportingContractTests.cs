using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's ThreadSupportingContractCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class ThreadSupportingContractTests
{
    [Test]
    public async Task ThreadMetadataAndDelegatesWorkWithoutThreads() => await Assert.That(ThreadSupportFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
