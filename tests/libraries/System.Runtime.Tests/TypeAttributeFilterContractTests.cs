using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's TypeAttributeFilterContractCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class TypeAttributeFilterContractTests
{
    [Test]
    public async Task AttributeFiltersMatchDesktop() => await Assert.That(TypeAttributeFilterFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
