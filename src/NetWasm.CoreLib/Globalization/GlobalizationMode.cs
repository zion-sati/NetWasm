// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Globalization
{
    // NetWasm always runs upstream's invariant globalization mode: culture data
    // and ICU are not part of the profile, and casing is InvariantModeCasing's.
    internal static class GlobalizationMode
    {
        internal static bool Invariant => true;
    }
}
