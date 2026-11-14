// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Net.NetworkInformation
{
    /// <summary>
    /// Specifies the current state of an IP address.
    /// </summary>
    public enum DuplicateAddressDetectionState
    {
        Invalid = 0,
        Tentative,
        Duplicate,
        Deprecated,
        Preferred,
    }
}
