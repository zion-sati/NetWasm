using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.CoreLib.Tests;

// Converted from the compiler harness's RuntimeEnvironmentMembersCompilationTests.cs: its fixture returns 42 for 41 on desktop .NET 11 and on NetWasm.
public sealed class RuntimeEnvironmentMembersTests
{
    [Test]
    public async Task MembersBehaveAsUpstream() => await Assert.That(RuntimeEnvironmentFixture.EntryPoint.Run(41)).IsEqualTo(42);
}
