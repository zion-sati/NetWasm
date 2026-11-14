// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Copied from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce src/libraries/System.Private.CoreLib/src/System/SearchValues/EmptySearchValues.cs

namespace System.Buffers
{
    internal sealed class EmptySearchValues<T> : SearchValues<T>
        where T : IEquatable<T>?
    {
        internal override T[] GetValues() =>
            [];

        internal override bool ContainsCore(T value) =>
            false;

        internal override int IndexOfAny(ReadOnlySpan<T> span) =>
            -1;

        internal override int IndexOfAnyExcept(ReadOnlySpan<T> span) =>
            span.IsEmpty ? -1 : 0;

        internal override int LastIndexOfAny(ReadOnlySpan<T> span) =>
            -1;

        internal override int LastIndexOfAnyExcept(ReadOnlySpan<T> span) =>
            span.Length - 1;

        internal override int IndexOfAnyMultiString(ReadOnlySpan<char> span) =>
            -1;
    }
}
