using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace NetWasm.Compiler.Core;

/// <summary>
/// Entries a consumer looks up by key: the one question it asks of a map. A compile's
/// units record or replay each lookup, so a consumer that asks only this depends only on
/// the entries it asked for.
/// </summary>
public interface IEntryLookup<TKey, TValue> where TKey : notnull
{
    bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value);
}

/// <summary>Items a consumer tests for membership: the one question it asks of a set.</summary>
public interface IMembership<T>
{
    bool Contains(T item);
}

/// <summary>A collection a consumer reads as a whole.</summary>
public interface IWholeRead<out T>
{
    T Read();
}

/// <summary>A map's entries, looked up by key.</summary>
public sealed class DictionaryLookup<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> entries) : IEntryLookup<TKey, TValue>
    where TKey : notnull
{
    private readonly IReadOnlyDictionary<TKey, TValue> _entries = entries ?? throw new ArgumentNullException(nameof(entries));

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _entries.TryGetValue(key, out value);
}

/// <summary>A set's items, tested for membership.</summary>
public sealed class SetMembership<T>(IReadOnlySet<T> items) : IMembership<T>
{
    private readonly IReadOnlySet<T> _items = items ?? throw new ArgumentNullException(nameof(items));

    public bool Contains(T item) => _items.Contains(item);
}

/// <summary>A collection read as a whole, as it is.</summary>
public sealed class WholeValue<T>(T value) : IWholeRead<T>
{
    public T Read() => value;
}

public static class Lookups
{
    public static IEntryLookup<TKey, TValue> Of<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> entries) where TKey : notnull =>
        new DictionaryLookup<TKey, TValue>(entries);

    public static IMembership<T> Of<T>(IReadOnlySet<T> items) => new SetMembership<T>(items);

    public static IWholeRead<T> Whole<T>(T value) => new WholeValue<T>(value);

    public static IEntryLookup<TKey, TValue> NoEntries<TKey, TValue>() where TKey : notnull => NoEntriesOf<TKey, TValue>.Lookup;

    public static IMembership<T> NoItems<T>() => NoItemsOf<T>.Membership;

    // The entry, which a consumer's plan guarantees: a missing one is a compiler defect.
    public static TValue Required<TKey, TValue>(this IEntryLookup<TKey, TValue> lookup, TKey key) where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(lookup);
        return lookup.TryGetValue(key, out var value)
            ? value
            : throw new KeyNotFoundException($"No entry for '{key}'.");
    }

    private static class NoEntriesOf<TKey, TValue> where TKey : notnull
    {
        public static readonly IEntryLookup<TKey, TValue> Lookup = new DictionaryLookup<TKey, TValue>(ImmutableDictionary<TKey, TValue>.Empty);
    }

    private static class NoItemsOf<T>
    {
        public static readonly IMembership<T> Membership = new SetMembership<T>(ImmutableHashSet<T>.Empty);
    }
}
