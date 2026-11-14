using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.ComponentModel.Tests;

// Converted from the compiler harness's BindingContractCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class BindingContractTests
{
    [Test]
    public async Task BindingContractsPreserveCallerDispatchAndSortValues() => await Assert.That(CategoryFixtureBindingContract.EntryPoint.Run(41)).IsEqualTo(42);
}
