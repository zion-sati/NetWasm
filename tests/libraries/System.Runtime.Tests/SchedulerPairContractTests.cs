using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's SchedulerPairContractCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class SchedulerPairContractTests
{
    [Test]
    public async Task PairValidatesArgumentsAndReadsTheSchedulersConcurrency() => await Assert.That(SchedulerPairFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
