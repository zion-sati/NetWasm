using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.ComponentModel.Tests;

// Converted from the compiler harness's ContextStackCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class ContextStackTests
{
    [Test]
    public async Task ContextStackPreservesOrderingAndAssignableLookup() => await Assert.That(StackFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
