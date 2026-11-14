// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce.
// NetWasm adaptation: CreateForMLDsa, CreateForSlhDsa and CreateForCompositeMLDsa are not ported.


namespace System.Security.Cryptography.X509Certificates
{
    public abstract class X509SignatureGenerator
    {
        private PublicKey? _publicKey;

        public PublicKey PublicKey => _publicKey ??= BuildPublicKey();

        public abstract byte[] GetSignatureAlgorithmIdentifier(HashAlgorithmName hashAlgorithm);
        public abstract byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm);
        protected abstract PublicKey BuildPublicKey();

        public static X509SignatureGenerator CreateForECDsa(ECDsa key)
        {
            ArgumentNullException.ThrowIfNull(key);

            return new ECDsaX509SignatureGenerator(key);
        }

        public static X509SignatureGenerator CreateForRSA(RSA key, RSASignaturePadding signaturePadding)
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(signaturePadding);

            if (signaturePadding == RSASignaturePadding.Pkcs1)
                return new RSAPkcs1X509SignatureGenerator(key);
            if (signaturePadding.Mode == RSASignaturePaddingMode.Pss)
                return new RSAPssX509SignatureGenerator(key, signaturePadding);

            throw new ArgumentException(SR.Cryptography_InvalidPaddingMode);
        }

    }
}
