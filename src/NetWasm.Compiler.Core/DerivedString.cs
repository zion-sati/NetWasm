using System;
using System.Threading;

namespace NetWasm.Compiler.Core;

/// <summary>
/// A string a record derives from some of its parts, kept with the parts it came from so
/// the record builds it once. A copy made with <c>with</c> shares the holder and derives
/// the string again only when its parts differ. Holders are all equal, so the record's
/// equality ignores it. The entry is published with a volatile write and taken with a
/// volatile read; threads that race derive the same string.
/// </summary>
internal sealed class DerivedString<TParts>
    where TParts : IEquatable<TParts>
{
    private Entry? _entry;

    internal string Get(TParts parts, Func<TParts, string> derive)
    {
        var entry = Volatile.Read(ref _entry);
        if (entry is not null && entry.Parts.Equals(parts))
        {
            return entry.Value;
        }
        var value = derive(parts);
        Volatile.Write(ref _entry, new Entry(parts, value));
        return value;
    }

    public override bool Equals(object? obj) => obj is DerivedString<TParts>;

    public override int GetHashCode() => 0;

    private sealed class Entry(TParts parts, string value)
    {
        internal TParts Parts { get; } = parts;
        internal string Value { get; } = value;
    }
}
