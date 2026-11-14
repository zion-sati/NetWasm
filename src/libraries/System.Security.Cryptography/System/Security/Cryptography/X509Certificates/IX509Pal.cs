// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce.

namespace System.Security.Cryptography.X509Certificates
{
    internal interface IX509Pal
    {
        AsymmetricAlgorithm DecodePublicKey(Oid oid, byte[] encodedKeyValue, byte[]? encodedParameters, ICertificatePal? certificatePal);
        ECDsa DecodeECDsaPublicKey(ICertificatePal? certificatePal);
        ECDiffieHellman DecodeECDiffieHellmanPublicKey(ICertificatePal? certificatePal);
        string X500DistinguishedNameDecode(byte[] encodedDistinguishedName, X500DistinguishedNameFlags flag);
        byte[] X500DistinguishedNameEncode(string distinguishedName, X500DistinguishedNameFlags flag);
        string X500DistinguishedNameFormat(byte[] encodedDistinguishedName, bool multiLine);
        X509ContentType GetCertContentType(ReadOnlySpan<byte> rawData);
        X509ContentType GetCertContentType(string fileName);
    }
}
