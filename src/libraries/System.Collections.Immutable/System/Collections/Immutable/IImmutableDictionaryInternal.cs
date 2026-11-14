// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Collections.Immutable
{
    internal interface IImmutableDictionaryInternal<TKey, TValue>
    {
        /// <summary>
        /// Determines whether the <see cref="ImmutableDictionary{TKey, TValue}"/>
        /// contains an element with the specified value.
        /// </summary>
        /// <param name="value">
        /// The value to locate in the <see cref="ImmutableDictionary{TKey, TValue}"/>.
        /// The value can be null for reference types.
        /// </param>
        /// <returns>
        /// true if the <see cref="ImmutableDictionary{TKey, TValue}"/> contains
        /// an element with the specified value; otherwise, false.
        /// </returns>
        bool ContainsValue(TValue value);
    }
}

