// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

using System.ComponentModel;

namespace System.Runtime.InteropServices
{
    // The enum of the return value of ICustomQueryInterface.GetInterface
    [EditorBrowsable(EditorBrowsableState.Never)]
    public enum CustomQueryInterfaceResult
    {
        Handled = 0,
        NotHandled = 1,
        Failed = 2,
    }
}
