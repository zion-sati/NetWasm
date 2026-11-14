// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.ComponentModel;

namespace System.Runtime.InteropServices
{
    // Wrapper that is converted to a variant with VT_BYREF | VT_VARIANT.
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class VariantWrapper
    {
        public VariantWrapper(object? obj)
        {
            WrappedObject = obj;
        }

        public object? WrappedObject { get; }
    }
}
