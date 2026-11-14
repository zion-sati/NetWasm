// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Globalization
{
    // NetWasm: the current culture as CultureInfo.OnCurrentCultureChanged last recorded it, null while the
    // invariant culture is current (the default). Code that must capture the current culture reads it here so
    // that capturing does not make CultureInfo, and the invariant CultureData its static initializer builds,
    // reachable.
    internal static class CurrentCultureMirror
    {
        internal static IFormatProvider? Culture;
    }
}
