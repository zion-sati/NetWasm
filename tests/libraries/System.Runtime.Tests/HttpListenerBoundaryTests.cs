using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's HttpListenerBoundaryCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class HttpListenerBoundaryTests
{
    [Test]
    public async Task ListenerDelegatesAndConstructionMatchDesktop() => await Assert.That(HttpListenerBoundaryFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
