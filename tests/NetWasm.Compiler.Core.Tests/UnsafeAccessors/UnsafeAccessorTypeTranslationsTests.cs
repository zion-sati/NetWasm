using System.Collections.Immutable;
using NetWasm.Compiler.Core.UnsafeAccessors;

namespace NetWasm.Compiler.Core.Tests.UnsafeAccessors;

public sealed class UnsafeAccessorTypeTranslationsTests
{
    [Fact]
    public void PositionsMapToTheReturnValueAndParameters()
    {
        var translations = new UnsafeAccessorTypeTranslations(["Return, A", null, "Second, B"]);

        Assert.Equal("Return, A", translations.Return);
        Assert.Null(translations.Parameter(0));
        Assert.Equal("Second, B", translations.Parameter(1));
        Assert.Null(translations.Parameter(2));
    }

    [Fact]
    public void AnEmptyListTranslatesNothing()
    {
        var translations = new UnsafeAccessorTypeTranslations([]);

        Assert.Null(translations.Return);
        Assert.Null(translations.Parameter(0));
    }

    // Declarations decoded twice from the same metadata compare equal, as method identities require.
    [Fact]
    public void EqualityComparesTheNames()
    {
        var first = new UnsafeAccessorTypeTranslations([null, "Owner, A"]);
        var same = new UnsafeAccessorTypeTranslations([null, "Owner, A"]);
        var other = new UnsafeAccessorTypeTranslations(["Owner, A"]);

        Assert.True(first.Equals(same));
        Assert.True(first.Equals((object)same));
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.False(first.Equals(other));
        Assert.False(first.Equals(null));
        Assert.False(first.Equals("Owner, A"));
        Assert.Equal(new UnsafeAccessorDeclaration(2, null, false, false, first),
            new UnsafeAccessorDeclaration(2, null, false, false, same));
        Assert.True(new UnsafeAccessorDeclaration(2, null, false, false, first).HasTypeTranslation);
        Assert.False(new UnsafeAccessorDeclaration(2, null, false, false).HasTypeTranslation);
    }

    [Fact]
    public void ANameListIsRequired() =>
        Assert.Throws<ArgumentException>(() => new UnsafeAccessorTypeTranslations(default(ImmutableArray<string?>)));
}
