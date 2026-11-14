using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.ComponentModel.Tests;

// Converted from the compiler harness's ComponentEventPayloadCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class ComponentEventPayloadTests
{
    [Test]
    public async Task EventPayloadsPreserveStateAndFailurePrecedence() => await Assert.That(CategoryFixtureComponentEventPayload.EntryPoint.Run(41)).IsEqualTo(42);
}
