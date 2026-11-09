// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's counterpart of upstream's TextInfo.Icu.cs and TextInfo.Nls.cs: the members the copied
// TextInfo.cs calls. NetWasm runs the invariant globalization mode only, so TextInfo's casing is
// its ASCII paths and InvariantModeCasing, and it never reaches the ICU or NLS casing.

namespace System.Globalization
{
    public partial class TextInfo
    {
        private IntPtr _sortHandle;

        private unsafe void IcuChangeCase(char* src, int srcLen, char* dstBuffer, int dstBufferCapacity, bool bToUpper) =>
            throw new PlatformNotSupportedException();

        private unsafe void NlsChangeCase(char* pSource, int pSourceLen, char* pResult, int pResultLen, bool toUpper) =>
            throw new PlatformNotSupportedException();

        // Upstream's OrdinalCasing.Icu.cs, ICU's ordinal casing table.
        private static class OrdinalCasing
        {
            internal static char ToUpper(char c) => throw new PlatformNotSupportedException();

            internal static char ToLower(char c) => throw new PlatformNotSupportedException();
        }
    }
}
