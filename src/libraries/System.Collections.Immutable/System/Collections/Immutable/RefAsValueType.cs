// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace System.Collections.Immutable
{
    [DebuggerDisplay("{Value,nq}")]
    internal struct RefAsValueType<T>
    {
        internal RefAsValueType(T value)
        {
            this.Value = value;
        }

        internal T Value;
    }
}
