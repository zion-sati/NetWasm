using System;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core.UnsafeAccessors;

// An accessor's UnsafeAccessorTypeAttribute names by signature position: the return value at index 0, then
// each parameter. A position without the attribute, or past the last annotated one, is not translated.
public sealed class UnsafeAccessorTypeTranslations : IEquatable<UnsafeAccessorTypeTranslations>
{
    public UnsafeAccessorTypeTranslations(ImmutableArray<string?> names)
    {
        if (names.IsDefault) throw new ArgumentException("The translations need a name list.", nameof(names));
        Names = names;
    }

    public ImmutableArray<string?> Names { get; }

    public string? Return => Names.IsEmpty ? null : Names[0];

    public string? Parameter(int index) => index + 1 < Names.Length ? Names[index + 1] : null;

    public bool Equals(UnsafeAccessorTypeTranslations? other) =>
        other is not null && Names.SequenceEqual(other.Names, StringComparer.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as UnsafeAccessorTypeTranslations);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var name in Names) hash.Add(name, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}
