using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Metadata.Tests;

// A local signature pins a local by its outermost pinned marker; a custom
// modifier keeps the pinning of the type it modifies, and a type named by a
// specification pins nothing by itself.
public sealed class PinnedLocalProviderTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AModifierKeepsThePinningOfItsType(bool pinned)
    {
        Assert.Equal(pinned, PinnedLocalProvider.Instance.GetModifiedType(false, pinned, isRequired: true));
    }

    [Fact]
    public void ASpecifiedTypeIsNotPinned()
    {
        Assert.False(PinnedLocalProvider.Instance.GetTypeFromSpecification(null!, null, default, 0));
    }
}
