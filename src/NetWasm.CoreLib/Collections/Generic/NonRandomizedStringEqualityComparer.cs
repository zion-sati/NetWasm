// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime at commit
// 474ed963699fc29809015392738a15231aaae2de. Legacy formatter serialization
// hooks are omitted with NetWasm's reflection-based formatter surface.

using System.Diagnostics;

namespace System.Collections.Generic
{
    internal class NonRandomizedStringEqualityComparer :
        IEqualityComparer<string?>,
        IInternalStringEqualityComparer
    {
        private static readonly NonRandomizedStringEqualityComparer WrappedAroundDefaultComparer = new OrdinalComparer(EqualityComparer<string?>.Default);
        private static readonly NonRandomizedStringEqualityComparer WrappedAroundStringComparerOrdinal = new OrdinalComparer((IEqualityComparer<string?>)(object)StringComparer.Ordinal);
        private static readonly NonRandomizedStringEqualityComparer WrappedAroundStringComparerOrdinalIgnoreCase = new OrdinalIgnoreCaseComparer((IEqualityComparer<string?>)(object)StringComparer.OrdinalIgnoreCase);

        private readonly IEqualityComparer<string?> _underlyingComparer;

        private NonRandomizedStringEqualityComparer(IEqualityComparer<string?> underlyingComparer)
        {
            Debug.Assert(underlyingComparer != null);
            _underlyingComparer = underlyingComparer;
        }

        public virtual bool Equals(string? x, string? y)
        {
            return string.Equals(x, y);
        }

        public virtual int GetHashCode(string? obj)
        {
            return obj?.GetNonRandomizedHashCode() ?? 0;
        }

        internal virtual RandomizedStringEqualityComparer GetRandomizedEqualityComparer()
        {
            return RandomizedStringEqualityComparer.Create(_underlyingComparer, ignoreCase: false);
        }

        public virtual IEqualityComparer<string?> GetUnderlyingEqualityComparer() =>
            _underlyingComparer;

        private sealed class OrdinalComparer :
            NonRandomizedStringEqualityComparer,
            IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
        {
            internal OrdinalComparer(IEqualityComparer<string?> wrappedComparer)
                : base(wrappedComparer)
            {
            }

            public override bool Equals(string? x, string? y) => string.Equals(x, y);

            public override int GetHashCode(string? obj)
            {
                Debug.Assert(obj != null, "This implementation is only called from first-party collection types that guarantee non-null parameters.");
                return obj.GetNonRandomizedHashCode();
            }

            int IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.GetHashCode(
                ReadOnlySpan<char> span) =>
                string.GetNonRandomizedHashCode(span);

            bool IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Equals(
                ReadOnlySpan<char> span,
                string? target)
            {
                if (span.IsEmpty && target is null)
                {
                    return false;
                }

                return span.SequenceEqual(target);
            }

            string IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Create(
                ReadOnlySpan<char> span) =>
                span.ToString();
        }

        private sealed class OrdinalIgnoreCaseComparer :
            NonRandomizedStringEqualityComparer,
            IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
        {
            internal OrdinalIgnoreCaseComparer(IEqualityComparer<string?> wrappedComparer)
                : base(wrappedComparer)
            {
            }

            public override bool Equals(string? x, string? y) =>
                string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

            public override int GetHashCode(string? obj)
            {
                Debug.Assert(obj != null, "This implementation is only called from first-party collection types that guarantee non-null parameters.");
                return obj.GetNonRandomizedHashCodeOrdinalIgnoreCase();
            }

            int IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.GetHashCode(
                ReadOnlySpan<char> span) =>
                string.GetNonRandomizedHashCodeOrdinalIgnoreCase(span);

            bool IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Equals(
                ReadOnlySpan<char> span,
                string? target)
            {
                if (span.IsEmpty && target is null)
                {
                    return false;
                }

                if (target is null || span.Length != target.Length)
                {
                    return false;
                }

                for (int index = 0; index < span.Length; index++)
                {
                    if (char.ToUpperInvariant(span[index]) != char.ToUpperInvariant(target[index]))
                    {
                        return false;
                    }
                }

                return true;
            }

            string IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Create(
                ReadOnlySpan<char> span) =>
                span.ToString();

            internal override RandomizedStringEqualityComparer GetRandomizedEqualityComparer()
            {
                return RandomizedStringEqualityComparer.Create(_underlyingComparer, ignoreCase: true);
            }
        }

        public static IEqualityComparer<string>? GetStringComparer(object comparer)
        {
            if (ReferenceEquals(comparer, EqualityComparer<string>.Default))
            {
                return WrappedAroundDefaultComparer;
            }

            if (ReferenceEquals(comparer, StringComparer.Ordinal))
            {
                return WrappedAroundStringComparerOrdinal;
            }

            if (ReferenceEquals(comparer, StringComparer.OrdinalIgnoreCase))
            {
                return WrappedAroundStringComparerOrdinalIgnoreCase;
            }

            return null;
        }
    }
}
