using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.ComponentModel.Tests;

// Converted from the compiler harness's BooleanComponentMetadataCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class BooleanComponentMetadataTests
{
    [Test]
    public async Task BooleanMetadataPreservesDefaultsAndEquality() => await Assert.That(BooleanMetadataFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
