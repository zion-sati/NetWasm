using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.Tests;

// Each typed-reference instruction calls one TypedReference helper; TypedReferenceCompilationTests runs them.
public sealed class TypedReferenceCallResolverTests
{
    [Fact]
    public void AnInstructionOutsideTheThreeHasNoHelper() =>
        Assert.Throws<ArgumentOutOfRangeException>("instruction", () =>
            new TypedReferenceCallResolver(null!, null!, null!, null!).Resolve((TypedReferenceInstruction)3, []));
}
