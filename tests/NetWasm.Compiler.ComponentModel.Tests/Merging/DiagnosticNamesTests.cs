using NetWasm.Compiler.ComponentModel.Merging;

namespace NetWasm.Compiler.ComponentModel.Tests.Merging;

// NETWASM_KEEP_NAMES asks for names with any value but an empty one.
public sealed class DiagnosticNamesTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("1", true)]
    [InlineData("0", true)]
    public void AnyNonEmptyValueKeepsNames(string? value, bool kept) => Assert.Equal(kept, DiagnosticNames.KeptFor(value));
}
