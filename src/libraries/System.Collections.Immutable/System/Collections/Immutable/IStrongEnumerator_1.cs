// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Collections.Immutable at commit
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.Collections.Generic;

namespace System.Collections.Immutable
{
    /// <summary>
    /// An <see cref="IEnumerator{T}"/>-like interface that does not derive from <see cref="IDisposable"/>.
    /// </summary>
    /// <typeparam name="T">The type of value to be enumerated.</typeparam>
    /// <remarks>
    /// This interface is useful because some enumerator struct types do not want to implement
    /// <see cref="IDisposable"/> since it increases the size of the generated code in foreach.
    /// </remarks>
    internal interface IStrongEnumerator<T>
    {
        /// <summary>
        /// Returns the current element.
        /// </summary>
        T Current { get; }

        /// <summary>
        /// Advances to the next element.
        /// </summary>
        bool MoveNext();
    }
}


