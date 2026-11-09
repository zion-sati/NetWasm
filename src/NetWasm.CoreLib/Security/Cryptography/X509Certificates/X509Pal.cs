// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// NetWasm adaptation: Instance is computed on each access instead of cached by a static
// initializer. NetWasm uses the browser PAL, whose BuildSingleton throws
// PlatformNotSupportedException, so callers see that exception directly instead of a
// TypeInitializationException.

namespace System.Security.Cryptography.X509Certificates
{
    internal partial class X509Pal
    {
        internal static IX509Pal Instance => BuildSingleton();

        private static partial IX509Pal BuildSingleton();
    }
}
