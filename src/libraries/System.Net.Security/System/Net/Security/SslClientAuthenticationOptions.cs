// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm: only the .NET Standard 2.1 members are ported: AllowTlsResume, the certificate context,
// CipherSuitesPolicy, CertificateChainPolicy and the RSA padding switches are newer. The
// default revocation mode is SslAuthenticationOptions.DefaultRevocationMode (NoCheck) inlined,
// since the internal SslAuthenticationOptions belongs to the deferred SslStream.

using System.Collections.Generic;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace System.Net.Security
{
    public class SslClientAuthenticationOptions
    {
        private EncryptionPolicy _encryptionPolicy = EncryptionPolicy.RequireEncryption;
        private X509RevocationMode _checkCertificateRevocation = X509RevocationMode.NoCheck;
        private SslProtocols _enabledSslProtocols = SslProtocols.None;
        private bool _allowRenegotiation = true;

        public bool AllowRenegotiation
        {
            get => _allowRenegotiation;
            set => _allowRenegotiation = value;
        }

        public LocalCertificateSelectionCallback? LocalCertificateSelectionCallback { get; set; }

        public RemoteCertificateValidationCallback? RemoteCertificateValidationCallback { get; set; }

        public List<SslApplicationProtocol>? ApplicationProtocols { get; set; }

        public string? TargetHost { get; set; }

        public X509CertificateCollection? ClientCertificates { get; set; }

        public X509RevocationMode CertificateRevocationCheckMode
        {
            get => _checkCertificateRevocation;
            set
            {
                if (value != X509RevocationMode.NoCheck && value != X509RevocationMode.Offline && value != X509RevocationMode.Online)
                {
                    throw new ArgumentException(SR.Format(SR.net_invalid_enum, nameof(X509RevocationMode)), nameof(value));
                }

                _checkCertificateRevocation = value;
            }
        }

        public EncryptionPolicy EncryptionPolicy
        {
            get => _encryptionPolicy;
            set
            {
#pragma warning disable SYSLIB0040 // NoEncryption and AllowNoEncryption are obsolete
                if (value != EncryptionPolicy.RequireEncryption && value != EncryptionPolicy.AllowNoEncryption && value != EncryptionPolicy.NoEncryption)
                {
                    throw new ArgumentException(SR.Format(SR.net_invalid_enum, nameof(EncryptionPolicy)), nameof(value));
                }
#pragma warning restore SYSLIB0040

                _encryptionPolicy = value;
            }
        }

        public SslProtocols EnabledSslProtocols
        {
            get => _enabledSslProtocols;
            set => _enabledSslProtocols = value;
        }
    }
}
