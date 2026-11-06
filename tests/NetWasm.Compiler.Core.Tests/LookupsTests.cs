using System.Collections.Immutable;

namespace NetWasm.Compiler.Core.Tests;

// A consumer asks a map for an entry, a set whether it holds an item, and a collection for
// the whole of it; each adapter answers from what it wraps, as it stands when asked.
public sealed class LookupsTests
{
    [Fact]
    public void ALookupFindsTheEntriesOfItsMapAsTheyStand()
    {
        var entries = new Dictionary<string, int> { ["a"] = 1 };
        var lookup = Lookups.Of(entries);

        Assert.True(lookup.TryGetValue("a", out var a));
        Assert.Equal(1, a);
        Assert.False(lookup.TryGetValue("b", out _));
        entries["b"] = 2;
        Assert.True(lookup.TryGetValue("b", out var b));
        Assert.Equal(2, b);
    }

    [Fact]
    public void AMembershipAnswersWhetherItsSetHoldsAnItem()
    {
        var membership = Lookups.Of(ImmutableHashSet.Create("a"));

        Assert.True(membership.Contains("a"));
        Assert.False(membership.Contains("b"));
    }

    [Fact]
    public void AWholeReadIsTheValueItWraps()
    {
        IReadOnlyList<int> items = [1, 2];

        Assert.Same(items, Lookups.Whole(items).Read());
    }

    [Fact]
    public void NoEntriesAndNoItemsFindNothing()
    {
        Assert.False(Lookups.NoEntries<string, int>().TryGetValue("a", out _));
        Assert.False(Lookups.NoItems<string>().Contains("a"));
        Assert.Same(Lookups.NoEntries<string, int>(), Lookups.NoEntries<string, int>());
        Assert.Same(Lookups.NoItems<string>(), Lookups.NoItems<string>());
    }

    [Fact]
    public void ARequiredEntryIsFoundOrNamedAsMissing()
    {
        var lookup = Lookups.Of(new Dictionary<string, int> { ["a"] = 1 });

        Assert.Equal(1, lookup.Required("a"));
        Assert.Equal("No entry for 'b'.", Assert.Throws<KeyNotFoundException>(() => lookup.Required("b")).Message);
        Assert.Throws<ArgumentNullException>(() => ((IEntryLookup<string, int>)null!).Required("a"));
    }

    [Fact]
    public void TheAdaptersNeedWhatTheyWrap()
    {
        Assert.Throws<ArgumentNullException>(() => Lookups.Of((IReadOnlyDictionary<string, int>)null!));
        Assert.Throws<ArgumentNullException>(() => Lookups.Of((IReadOnlySet<string>)null!));
    }
}
