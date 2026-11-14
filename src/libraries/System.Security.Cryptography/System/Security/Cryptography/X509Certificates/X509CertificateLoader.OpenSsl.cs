// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: dotnet/runtime deae2e32's (.NET 11.0.0) loader, internal (X509CertificateLoader is .NET 9 surface), without
// the ML-KEM, ML-DSA and SLH-DSA keys.

using System.Diagnostics;
using Internal.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace System.Security.Cryptography.X509Certificates
{
    internal static partial class X509CertificateLoader
    {
        private static partial ICertificatePal LoadCertificatePal(ReadOnlySpan<byte> data)
        {
            ICertificatePal? pal;

            if (OpenSslX509CertificateReader.TryReadX509Der(data, out pal) ||
                OpenSslX509CertificateReader.TryReadX509Pem(data, out pal))
            {
                return pal;
            }

            throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding);
        }

        private static partial ICertificatePal LoadCertificatePalFromFile(string path)
        {
            ICertificatePal? pal;

            using (SafeBioHandle fileBio = Interop.Crypto.BioNewFile(path, "rb"))
            {
                Interop.Crypto.CheckValidOpenSslHandle(fileBio);

                int bioPosition = Interop.Crypto.BioTell(fileBio);
                Debug.Assert(bioPosition >= 0);

                if (!OpenSslX509CertificateReader.TryReadX509Der(fileBio, out pal))
                {
                    OpenSslX509CertificateReader.RewindBio(fileBio, bioPosition);

                    if (!OpenSslX509CertificateReader.TryReadX509Pem(fileBio, out pal))
                    {
                        throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding);
                    }
                }
            }

            return pal;
        }

        private static partial Pkcs12Return FromCertAndKey(CertAndKey certAndKey, ImportState importState)
        {
            OpenSslX509CertificateReader pal = (OpenSslX509CertificateReader)certAndKey.Cert!;

            if (certAndKey.Key is not null)
            {
                pal.SetPrivateKey(GetPrivateKey(certAndKey.Key));
                certAndKey.Key.Dispose();
            }

            return new Pkcs12Return(pal);
        }

        private static partial Pkcs12Key? CreateKey(string algorithm, ReadOnlySpan<byte> pkcs8)
        {
            switch (algorithm)
            {
                case Oids.Rsa or Oids.RsaPss:
                    return new AsymmetricAlgorithmPkcs12PrivateKey(pkcs8, static () => new RSAOpenSsl());
                case Oids.EcPublicKey or Oids.EcDiffieHellman:
                    return new AsymmetricAlgorithmPkcs12PrivateKey(pkcs8, static () => new ECDiffieHellmanOpenSsl());
                case Oids.Dsa:
                    return new AsymmetricAlgorithmPkcs12PrivateKey(pkcs8, static () => new DSAOpenSsl());
                default:
                    return null;
            }
        }

        internal static SafeEvpPKeyHandle GetPrivateKey(Pkcs12Key key)
        {
            if (key.Key is RSAOpenSsl rsa)
            {
                return rsa.DuplicateKeyHandle();
            }

            if (key.Key is DSAOpenSsl dsa)
            {
                return dsa.DuplicateKeyHandle();
            }


            return ((ECDiffieHellmanOpenSsl)key.Key).DuplicateKeyHandle();
        }

        private static partial ICertificatePalCore LoadX509Der(ReadOnlyMemory<byte> data)
        {
            if (OpenSslX509CertificateReader.TryReadX509Der(data.Span, out ICertificatePal? ret))
            {
                return ret;
            }

            throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding);
        }
    }
}
