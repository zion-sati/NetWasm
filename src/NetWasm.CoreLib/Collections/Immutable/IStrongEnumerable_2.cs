// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// 474ed963699fc29809015392738a15231aaae2de.

using System.Collections.Generic;

namespace System.Collections.Immutable
{
    /// <summary>
    /// An interface implemented by collections that expose a strongly typed
    /// enumerator without boxing it.
    /// </summary>
    /// <typeparam name="T">The type of value to be enumerated.</typeparam>
    /// <typeparam name="TEnumerator">The type of the enumerator struct.</typeparam>
    internal interface IStrongEnumerable<out T, TEnumerator>
        where TEnumerator : struct, IStrongEnumerator<T>
    {
        /// <summary>
        /// Gets the strongly-typed enumerator.
        /// </summary>
        /// <returns></returns>
        TEnumerator GetEnumerator();
    }
}

