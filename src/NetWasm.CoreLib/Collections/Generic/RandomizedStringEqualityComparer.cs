// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Collections.Generic
{
    internal abstract class RandomizedStringEqualityComparer :
        EqualityComparer<string?>,
        IInternalStringEqualityComparer
    {
        private readonly MarvinSeed _seed;
        private readonly IEqualityComparer<string?> _underlyingComparer;

        private RandomizedStringEqualityComparer(
            IEqualityComparer<string?> underlyingComparer)
        {
            _underlyingComparer = underlyingComparer;
            ulong seed = MarvinSeedRuntime.Get();
            _seed.P0 = (uint)seed;
            _seed.P1 = (uint)(seed >> 32);
        }

        internal static RandomizedStringEqualityComparer Create(
            IEqualityComparer<string?> underlyingComparer,
            bool ignoreCase) =>
            ignoreCase
                ? new OrdinalIgnoreCaseComparer(underlyingComparer)
                : new OrdinalComparer(underlyingComparer);

        public IEqualityComparer<string?> GetUnderlyingEqualityComparer() =>
            _underlyingComparer;

        private struct MarvinSeed
        {
            internal uint P0;
            internal uint P1;
        }

        private sealed class OrdinalComparer :
            RandomizedStringEqualityComparer,
            IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
        {
            internal OrdinalComparer(IEqualityComparer<string?> wrappedComparer)
                : base(wrappedComparer)
            {
            }

            string IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Create(
                ReadOnlySpan<char> span) =>
                span.ToString();

            public override bool Equals(string? x, string? y) => string.Equals(x, y);

            bool IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Equals(
                ReadOnlySpan<char> alternate,
                string? other)
            {
                if (alternate.IsEmpty && other is null)
                {
                    return false;
                }

                return alternate.SequenceEqual(other);
            }

            public override unsafe int GetHashCode(string? obj)
            {
                if (obj is null)
                {
                    return 0;
                }

                fixed (char* data = obj)
                {
                    return Marvin.ComputeHash32(
                        ref *(byte*)data,
                        (uint)obj.Length * 2,
                        _seed.P0,
                        _seed.P1);
                }
            }

            int IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.GetHashCode(
                ReadOnlySpan<char> alternate) =>
                Marvin.ComputeHash32(
                    ref Unsafe.As<char, byte>(ref MemoryMarshal.GetReference(alternate)),
                    (uint)alternate.Length * 2,
                    _seed.P0,
                    _seed.P1);
        }

        private sealed class OrdinalIgnoreCaseComparer :
            RandomizedStringEqualityComparer,
            IAlternateEqualityComparer<ReadOnlySpan<char>, string?>
        {
            internal OrdinalIgnoreCaseComparer(IEqualityComparer<string?> wrappedComparer)
                : base(wrappedComparer)
            {
            }

            string IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Create(
                ReadOnlySpan<char> span) =>
                span.ToString();

            public override bool Equals(string? x, string? y) =>
                string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

            bool IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.Equals(
                ReadOnlySpan<char> alternate,
                string? other)
            {
                if (alternate.IsEmpty && other is null)
                {
                    return false;
                }

                if (other is null || alternate.Length != other.Length)
                {
                    return false;
                }

                for (int index = 0; index < alternate.Length; index++)
                {
                    if (char.ToUpperInvariant(alternate[index]) != char.ToUpperInvariant(other[index]))
                    {
                        return false;
                    }
                }

                return true;
            }

            public override unsafe int GetHashCode(string? obj)
            {
                if (obj is null)
                {
                    return 0;
                }

                fixed (char* data = obj)
                {
                    return Marvin.ComputeHash32OrdinalIgnoreCase(
                        ref *data,
                        obj.Length,
                        _seed.P0,
                        _seed.P1);
                }
            }

            int IAlternateEqualityComparer<ReadOnlySpan<char>, string?>.GetHashCode(
                ReadOnlySpan<char> alternate) =>
                Marvin.ComputeHash32OrdinalIgnoreCase(
                    ref MemoryMarshal.GetReference(alternate),
                    alternate.Length,
                    _seed.P0,
                    _seed.P1);
        }
    }
}
