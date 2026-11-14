using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's SafeHandleMarshallerCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class SafeHandleMarshallerTests
{
    [Test]
    public async Task MarshallersCreateAdoptAndReleaseHandlesAsDesktop() => await Assert.That(SafeHandleMarshallerFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
