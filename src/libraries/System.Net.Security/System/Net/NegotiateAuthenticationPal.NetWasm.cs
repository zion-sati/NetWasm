// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's NegotiateAuthenticationPal factory, in place of upstream's per-platform ones (GSSAPI on Unix, the managed
// NTLM and SPNEGO on Android and tvOS). WASI has no GSSAPI, so every package reports Unsupported, as upstream's
// UnsupportedNegotiateAuthenticationPal does on a platform without one; callers such as SmtpClient fall back.

using System.Net.Security;

namespace System.Net
{
    internal abstract partial class NegotiateAuthenticationPal
    {
        public static NegotiateAuthenticationPal Create(NegotiateAuthenticationClientOptions clientOptions) =>
            new UnsupportedNegotiateAuthenticationPal(clientOptions);

        public static NegotiateAuthenticationPal Create(NegotiateAuthenticationServerOptions serverOptions) =>
            new UnsupportedNegotiateAuthenticationPal(serverOptions);
    }
}
