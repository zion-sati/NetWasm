// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Net.Security
{
    internal static class SslClientAuthenticationOptionsExtensions
    {
        // NetWasm: NetWasm's SslClientAuthenticationOptions has the .NET Standard 2.1 members (wasi:tls takes none of
        // .NET 5's certificate contexts, chain or cipher suite policies, or padding choices), and no reflection checks the
        // copy in Debug builds.
        public static SslClientAuthenticationOptions ShallowClone(this SslClientAuthenticationOptions options)
        {
            var clone = new SslClientAuthenticationOptions()
            {
                AllowRenegotiation = options.AllowRenegotiation,
                ApplicationProtocols = options.ApplicationProtocols != null ? new List<SslApplicationProtocol>(options.ApplicationProtocols) : null,
                CertificateRevocationCheckMode = options.CertificateRevocationCheckMode,
                ClientCertificates = options.ClientCertificates,
                EnabledSslProtocols = options.EnabledSslProtocols,
                EncryptionPolicy = options.EncryptionPolicy,
                LocalCertificateSelectionCallback = options.LocalCertificateSelectionCallback,
                RemoteCertificateValidationCallback = options.RemoteCertificateValidationCallback,
                TargetHost = options.TargetHost,
            };

            return clone;
        }
    }
}
