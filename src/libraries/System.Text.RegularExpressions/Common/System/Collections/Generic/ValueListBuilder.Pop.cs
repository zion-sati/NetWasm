// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Collections.Generic
{
    /// <summary>
    /// These public methods are required by RegexWriter.
    /// </summary>
    internal ref partial struct ValueListBuilder<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Pop()
        {
            _pos--;
            return _span[_pos];
        }
    }
}
