using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's FloatingFormatDifferentialCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class FloatingFormatDifferentialTests
{
    [Test]
    public async Task FloatingFormattingAndParsingMatchDesktop() => await Assert.That(FloatFormatFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
