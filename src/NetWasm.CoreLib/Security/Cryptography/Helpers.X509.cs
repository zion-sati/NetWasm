// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de
// (System.Security.Cryptography/src/System/Security/Cryptography/Helpers.cs and
// Common/src/System/Security/Cryptography/Helpers.cs). NetWasm adaptation: only the members the
// X.509 sources use. SR is aliased because CoreLib also holds System.Formats.Asn1's SR, which a
// separate assembly owns upstream.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Asn1;
using System.Globalization;
using System.Security.Cryptography;
using SR = System.Security.Cryptography.SR;

namespace Internal.Cryptography
{
    internal static partial class Helpers
    {
        internal static void AddRange<T>(this ICollection<T> coll, IEnumerable<T> newData)
        {
            foreach (T datum in newData)
            {
                coll.Add(datum);
            }
        }


        // Encode a byte array as an array of upper-case hex characters.
        internal static char[] ToHexArrayUpper(this byte[] bytes)
        {
            char[] chars = new char[bytes.Length * 2];
            HexConverter.EncodeToUtf16(bytes, chars);
            return chars;
        }

        // Encode a byte array as an upper case hex string.
        internal static string ToHexStringUpper(this byte[] bytes) =>
            Convert.ToHexString(bytes);

        // Decode a hex string-encoded byte array passed to various X509 crypto api.
        // The parsing rules are overly forgiving but for compat reasons, they cannot be tightened.
        internal static byte[] LaxDecodeHexString(this string hexString)
        {
            int whitespaceCount = 0;

            ReadOnlySpan<char> s = hexString;

            if (s.StartsWith('\u200E'))
            {
                s = s.Slice(1);
            }

            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsWhiteSpace(s[i]))
                    whitespaceCount++;
            }

            uint cbHex = (uint)(s.Length - whitespaceCount) / 2;
            byte[] hex = new byte[cbHex];
            byte accum = 0;
            bool byteInProgress = false;
            int index = 0;

            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];

                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                accum <<= 4;
                accum |= (byte)HexConverter.FromChar(c);

                byteInProgress = !byteInProgress;

                // If we've flipped from 0 to 1, back to 0, we have a whole byte
                // so add it to the buffer.
                if (!byteInProgress)
                {
                    Debug.Assert(index < cbHex);

                    hex[index] = accum;
                    index++;
                }
            }

            // .NET Framework compat:
            // The .NET Framework algorithm removed all whitespace before the loop, then went up to length/2
            // of what was left.  This means that in the event of odd-length input the last char is
            // ignored, no exception should be raised.
            Debug.Assert(index == cbHex);

            return hex;
        }

        internal static bool ContentsEqual(this byte[]? a1, byte[]? a2)
        {
            if (a1 == null)
            {
                return a2 == null;
            }

            if (a2 == null || a1.Length != a2.Length)
            {
                return false;
            }

            return a1.AsSpan().SequenceEqual(a2);
        }

        internal static bool AreSamePublicECParameters(ECParameters aParameters, ECParameters bParameters)
        {
            if (aParameters.Curve.CurveType != bParameters.Curve.CurveType)
                return false;

            if (!aParameters.Q.X!.ContentsEqual(bParameters.Q.X!) ||
                !aParameters.Q.Y!.ContentsEqual(bParameters.Q.Y!))
            {
                return false;
            }

            ECCurve aCurve = aParameters.Curve;
            ECCurve bCurve = bParameters.Curve;

            if (aCurve.IsNamed)
            {
                // On Windows we care about FriendlyName, on Unix we care about Value
                return aCurve.Oid.Value == bCurve.Oid.Value &&
                    string.Equals(aCurve.Oid.FriendlyName, bCurve.Oid.FriendlyName, StringComparison.OrdinalIgnoreCase);
            }

            if (!aCurve.IsExplicit)
            {
                // Implicit curve, always fail.
                return false;
            }

            // Ignore Cofactor (which is derivable from the prime or polynomial and Order)
            // Ignore Seed and Hash (which are entirely optional, and about how A and B were built)
            if (!aCurve.G.X!.ContentsEqual(bCurve.G.X!) ||
                !aCurve.G.Y!.ContentsEqual(bCurve.G.Y!) ||
                !aCurve.Order.ContentsEqual(bCurve.Order) ||
                !aCurve.A.ContentsEqual(bCurve.A) ||
                !aCurve.B.ContentsEqual(bCurve.B))
            {
                return false;
            }

            if (aCurve.IsPrime)
            {
                return aCurve.Prime.ContentsEqual(bCurve.Prime);
            }

            if (aCurve.IsCharacteristic2)
            {
                return aCurve.Polynomial.ContentsEqual(bCurve.Polynomial);
            }

            Debug.Fail($"Missing match criteria for curve type {aCurve.CurveType}");
            return false;
        }

        internal static bool IsValidDay(this Calendar calendar, int year, int month, int day, int era)
        {
            return (calendar.IsValidMonth(year, month, era) && day >= 1 && day <= calendar.GetDaysInMonth(year, month, era));
        }

        private static bool IsValidMonth(this Calendar calendar, int year, int month, int era)
        {
            return (calendar.IsValidYear(year) && month >= 1 && month <= calendar.GetMonthsInYear(year, era));
        }

        private static bool IsValidYear(this Calendar calendar, int year)
        {
            return (year >= calendar.GetYear(calendar.MinSupportedDateTime) && year <= calendar.GetYear(calendar.MaxSupportedDateTime));
        }

        internal static unsafe void ValidateDer(ReadOnlySpan<byte> encodedValue)
        {
            try
            {
                const int StackQueueSize = 16;
                Span<(int Offset, int Length)> stack = stackalloc (int, int)[StackQueueSize];
                int stackCount = 0;

                stack[stackCount++] = (0, encodedValue.Length);

                do
                {
                    (int offset, int length) = stack[--stackCount];

                    ValueAsnReader reader = new ValueAsnReader(
                        encodedValue.Slice(offset, length),
                        AsnEncodingRules.DER);

                    while (reader.HasData)
                    {
                        Asn1Tag tag = reader.PeekTag();

                        // If the tag is in the UNIVERSAL class
                        //
                        // DER limits the constructed encoding to SEQUENCE and SET, as well as anything which gets
                        // a defined encoding as being an IMPLICIT SEQUENCE.
                        if (tag.TagClass == TagClass.Universal)
                        {
                            switch ((UniversalTagNumber)tag.TagValue)
                            {
                                case UniversalTagNumber.External:
                                case UniversalTagNumber.Embedded:
                                case UniversalTagNumber.Sequence:
                                case UniversalTagNumber.Set:
                                case UniversalTagNumber.UnrestrictedCharacterString:
                                    if (!tag.IsConstructed)
                                    {
                                        throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding);
                                    }

                                    break;
                                default:
                                    if (tag.IsConstructed)
                                    {
                                        throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding);
                                    }

                                    break;
                            }
                        }

                        if (tag.IsConstructed)
                        {
                            ReadOnlySpan<byte> content = reader.PeekContentBytes();

                            if (content.Length > 0)
                            {
                                if (!encodedValue.Overlaps(content, out int contentOffset))
                                {
                                    Debug.Fail("Contents do not overlap original span");
                                    throw new CryptographicException();
                                }

                                if (stackCount == stack.Length)
                                {
                                    Span<(int, int)> nextStack = new (int, int)[stack.Length * 2];
                                    stack.CopyTo(nextStack);
                                    stack = nextStack;
                                }

                                stack[stackCount++] = (contentOffset, content.Length);
                            }
                        }

                        // Skip past the current value.
                        reader.ReadEncodedValue();
                    }
                } while (stackCount > 0);
            }
            catch (AsnContentException e)
            {
                throw new CryptographicException(SR.Cryptography_Der_Invalid_Encoding, e);
            }
        }

        internal static ReadOnlyMemory<byte>? ToNullableMemory(this byte[]? array)
        {
            if (array is null)
            {
                return default(ReadOnlyMemory<byte>?);
            }

            return array;
        }

        internal static bool HashAlgorithmRequired(string? keyAlgorithm)
        {
            // This list could either be written as "ML-DSA and friends return false",
            // or "RSA and friends return true".
            //
            // The consequences of returning true is that the hashAlgorithm parameter
            // gets pre-validated to not be null or empty, which means false positives
            // impact new ML-DSA-like algorithms.
            //
            // The consequences of returning false is that the hashAlgorithm parameter
            // is not pre-validated.  That just means that in a false negative the user
            // gets probably the same exception, but from a different callstack.
            //
            // False positives or negatives are not possible with the simple Build that takes
            // only an X509Certificate2, as we control the destiny there entirely, it's only
            // for the power user scenario of the X509SignatureGenerator that this is a concern.
            //
            // Since the false-positive is worse than the false-negative, the list is written
            // as explicit-true, implicit-false.
            return keyAlgorithm switch
            {
                Oids.Rsa or
                Oids.RsaPss or
                Oids.EcPublicKey or
                Oids.Dsa => true,
                _ => false,
            };
        }
    }
}
