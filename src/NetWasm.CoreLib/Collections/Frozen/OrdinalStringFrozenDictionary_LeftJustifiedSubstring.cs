// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable Frozen string specialization at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Collections.Generic;

namespace System.Collections.Frozen
{
    internal sealed partial class OrdinalStringFrozenDictionary_LeftJustifiedSubstring<TValue> : OrdinalStringFrozenDictionary<TValue>
    {
        internal OrdinalStringFrozenDictionary_LeftJustifiedSubstring(
            string[] keys,
            TValue[] values,
            IEqualityComparer<string> comparer,
            int minimumLength,
            int maximumLengthDiff,
            int hashIndex,
            int hashCount)
            : base(keys, values, comparer, minimumLength, maximumLengthDiff, hashIndex, hashCount)
        {
        }

        // See comment in OrdinalStringFrozenDictionary for why these overrides exist. Do not remove.
        private protected override ref readonly TValue GetValueRefOrNullRefCore(string key) => ref base.GetValueRefOrNullRefCore(key);

        private protected override bool Equals(string? x, string? y) => string.Equals(x, y);
        private protected override bool Equals(ReadOnlySpan<char> x, string? y) => x.SequenceEqual(y.AsSpan());
        private protected override int GetHashCode(string s) => Hashing.GetHashCodeOrdinal(s.AsSpan(HashIndex, HashCount));
        private protected override int GetHashCode(ReadOnlySpan<char> s) => Hashing.GetHashCodeOrdinal(s.Slice(HashIndex, HashCount));
    }
}
