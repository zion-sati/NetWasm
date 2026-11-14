using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's ParallelOptionsContractCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class ParallelOptionsContractTests
{
    [Test]
    public async Task OptionsValidationAndDefaultResultWork() => await Assert.That(ParallelOptionsFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
