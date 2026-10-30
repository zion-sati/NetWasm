// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de src/libraries/System.Private.CoreLib/src/System/SearchValues/Any2CharPackedSearchValues.cs

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;

namespace System.Buffers
{
    internal sealed class Any2CharPackedSearchValues : SearchValues<char>
    {
        private readonly char _e0, _e1;

        public Any2CharPackedSearchValues(char value0, char value1) =>
            (_e0, _e1) = (value0, value1);

        internal override char[] GetValues() =>
            [_e0, _e1];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool ContainsCore(char value) =>
            value == _e0 || value == _e1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CompExactlyDependsOn(typeof(Sse2))]
        internal override int IndexOfAny(ReadOnlySpan<char> span) =>
            PackedSpanHelpers.IndexOfAny(ref MemoryMarshal.GetReference(span), _e0, _e1, span.Length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CompExactlyDependsOn(typeof(Sse2))]
        internal override int IndexOfAnyExcept(ReadOnlySpan<char> span) =>
            PackedSpanHelpers.IndexOfAnyExcept(ref MemoryMarshal.GetReference(span), _e0, _e1, span.Length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override int LastIndexOfAny(ReadOnlySpan<char> span) =>
            span.LastIndexOfAny(_e0, _e1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override int LastIndexOfAnyExcept(ReadOnlySpan<char> span) =>
            span.LastIndexOfAnyExcept(_e0, _e1);
    }
}
