using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's NumberFormatInfoParsingCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class NumberFormatInfoParsingTests
{
    [Test]
    public async Task NumericParsingHonoursTheProvidedNumberFormatInfo() => await Assert.That(NumberFormatInfoParsingFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
