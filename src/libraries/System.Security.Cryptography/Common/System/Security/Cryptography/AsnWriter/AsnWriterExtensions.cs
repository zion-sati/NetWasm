// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime deae2e32bf0e018e09d64597b7c59b62b73bb3ce
// (Common/src/System/Security/Cryptography/AsnWriter/AsnWriterExtensions.cs).

using System.Security.Cryptography;

namespace System.Formats.Asn1
{
    internal static class AsnWriterExtensions
    {
        internal static void WriteEncodedValueForCrypto(
            this AsnWriter writer,
            ReadOnlySpan<byte> value)
        {
            try
            {
                writer.WriteEncodedValue(value);
            }
            catch (ArgumentException e)
            {
                throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding, e);
            }
        }

        internal static void WriteObjectIdentifierForCrypto(
            this AsnWriter writer,
            string value)
        {
            try
            {
                writer.WriteObjectIdentifier(value);
            }
            catch (ArgumentException e)
            {
                throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding, e);
            }
        }
    }
}
