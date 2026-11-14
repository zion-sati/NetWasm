using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.ComponentModel.Tests;

// Converted from the compiler harness's MemberDescriptorCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class MemberDescriptorTests
{
    [Test]
    public async Task SuppliedDescriptorsPreserveMetadataBehavior() => await Assert.That(DescriptorFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
