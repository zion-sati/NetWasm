using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's ReflectionPrimitivesCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class ReflectionPrimitivesTests
{
    [Test]
    public async Task OpCodesAndLabelsMatchUpstreamData() => await Assert.That(ReflectionPrimitivesFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
