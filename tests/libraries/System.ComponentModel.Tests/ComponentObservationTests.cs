using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.ComponentModel.Tests;

// Converted from the compiler harness's ComponentObservationCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class ComponentObservationTests
{
    [Test]
    public async Task CallerDefinedObservationContractsPreserveStateAndEvents() => await Assert.That(ObservationFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
