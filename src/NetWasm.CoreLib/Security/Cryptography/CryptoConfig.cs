// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime 474ed963699fc29809015392738a15231aaae2de.
// NetWasm adaptation: CreateFromName uses upstream's fixed BROWSER switch, extended with the
// desktop RandomNumberGenerator names because RNG is supported here, and with the RSA PKCS#1 and
// DSA signature formatter/deformatter class names that upstream's non-browser build resolves
// through its class-name fallback, so SignatureDescription can create them (owner decision
// 2026-10-21). Runtime type activation
// (AddAlgorithm, class-name lookup, constructor binding) is unavailable without reflection.
// AddOID, EncodeOID and MapNameToOID use the non-browser managed bodies, with upstream's default OID table.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.Versioning;

namespace System.Security.Cryptography
{
    public class CryptoConfig
    {
        internal const string CreateFromNameUnreferencedCodeMessage = "The default algorithm implementations might be removed, use strong type references like 'RSA.Create()' instead.";

        private const string OID_RSA_SMIMEalgCMS3DESwrap = "1.2.840.113549.1.9.16.3.6";
        private const string OID_RSA_MD5 = "1.2.840.113549.2.5";
        private const string OID_RSA_RC2CBC = "1.2.840.113549.3.2";
        private const string OID_RSA_DES_EDE3_CBC = "1.2.840.113549.3.7";
        private const string OID_OIWSEC_desCBC = "1.3.14.3.2.7";
        private const string OID_OIWSEC_SHA1 = "1.3.14.3.2.26";
        private const string OID_OIWSEC_SHA256 = "2.16.840.1.101.3.4.2.1";
        private const string OID_OIWSEC_SHA384 = "2.16.840.1.101.3.4.2.2";
        private const string OID_OIWSEC_SHA512 = "2.16.840.1.101.3.4.2.3";
        private const string OID_OIWSEC_RIPEMD160 = "1.3.36.3.2.1";

        private static Dictionary<string, string>? s_defaultOidHT;
        private static readonly ConcurrentDictionary<string, string> appOidHT = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, string> DefaultOidHT
        {
            get
            {
                if (s_defaultOidHT != null)
                {
                    return s_defaultOidHT;
                }

                int capacity = 37;
                Dictionary<string, string> ht = new Dictionary<string, string>(capacity, StringComparer.OrdinalIgnoreCase);

                ht.Add("SHA", OID_OIWSEC_SHA1);
                ht.Add("SHA1", OID_OIWSEC_SHA1);
                ht.Add("System.Security.Cryptography.SHA1", OID_OIWSEC_SHA1);
                ht.Add("System.Security.Cryptography.SHA1CryptoServiceProvider", OID_OIWSEC_SHA1);
                ht.Add("System.Security.Cryptography.SHA1Cng", OID_OIWSEC_SHA1);
                ht.Add("System.Security.Cryptography.SHA1Managed", OID_OIWSEC_SHA1);

                ht.Add("SHA256", OID_OIWSEC_SHA256);
                ht.Add("System.Security.Cryptography.SHA256", OID_OIWSEC_SHA256);
                ht.Add("System.Security.Cryptography.SHA256CryptoServiceProvider", OID_OIWSEC_SHA256);
                ht.Add("System.Security.Cryptography.SHA256Cng", OID_OIWSEC_SHA256);
                ht.Add("System.Security.Cryptography.SHA256Managed", OID_OIWSEC_SHA256);

                ht.Add("SHA384", OID_OIWSEC_SHA384);
                ht.Add("System.Security.Cryptography.SHA384", OID_OIWSEC_SHA384);
                ht.Add("System.Security.Cryptography.SHA384CryptoServiceProvider", OID_OIWSEC_SHA384);
                ht.Add("System.Security.Cryptography.SHA384Cng", OID_OIWSEC_SHA384);
                ht.Add("System.Security.Cryptography.SHA384Managed", OID_OIWSEC_SHA384);

                ht.Add("SHA512", OID_OIWSEC_SHA512);
                ht.Add("System.Security.Cryptography.SHA512", OID_OIWSEC_SHA512);
                ht.Add("System.Security.Cryptography.SHA512CryptoServiceProvider", OID_OIWSEC_SHA512);
                ht.Add("System.Security.Cryptography.SHA512Cng", OID_OIWSEC_SHA512);
                ht.Add("System.Security.Cryptography.SHA512Managed", OID_OIWSEC_SHA512);

                ht.Add("RIPEMD160", OID_OIWSEC_RIPEMD160);
                ht.Add("System.Security.Cryptography.RIPEMD160", OID_OIWSEC_RIPEMD160);
                ht.Add("System.Security.Cryptography.RIPEMD160Managed", OID_OIWSEC_RIPEMD160);

                ht.Add("MD5", OID_RSA_MD5);
                ht.Add("System.Security.Cryptography.MD5", OID_RSA_MD5);
                ht.Add("System.Security.Cryptography.MD5CryptoServiceProvider", OID_RSA_MD5);
                ht.Add("System.Security.Cryptography.MD5Managed", OID_RSA_MD5);

                ht.Add("TripleDESKeyWrap", OID_RSA_SMIMEalgCMS3DESwrap);

                ht.Add("RC2", OID_RSA_RC2CBC);
                ht.Add("System.Security.Cryptography.RC2CryptoServiceProvider", OID_RSA_RC2CBC);

                ht.Add("DES", OID_OIWSEC_desCBC);
                ht.Add("System.Security.Cryptography.DESCryptoServiceProvider", OID_OIWSEC_desCBC);

                ht.Add("TripleDES", OID_RSA_DES_EDE3_CBC);
                ht.Add("System.Security.Cryptography.TripleDESCryptoServiceProvider", OID_RSA_DES_EDE3_CBC);

                Debug.Assert(ht.Count <= capacity); // if more entries are added in the future, increase initial capacity.
                s_defaultOidHT = ht;
                return s_defaultOidHT;
            }
        }

        // .NET does not support AllowOnlyFipsAlgorithms
        public static bool AllowOnlyFipsAlgorithms => false;

        [UnsupportedOSPlatform("browser")]
        public static void AddAlgorithm(Type algorithm, params string[] names)
        {
            throw new PlatformNotSupportedException(SR.SystemSecurityCryptography_PlatformNotSupported);
        }

        [RequiresUnreferencedCode("The default algorithm implementations might be removed, use strong type references like 'RSA.Create()' instead.")]
        public static object? CreateFromName(string name, params object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(name);

            switch (name)
            {
#pragma warning disable SYSLIB0021 // Obsolete: derived cryptographic types
                // hardcode mapping for SHA* and HMAC* algorithm names from https://learn.microsoft.com/dotnet/api/system.security.cryptography.cryptoconfig?view=net-5.0#remarks
                case "SHA":
                case "SHA1":
                case "System.Security.Cryptography.SHA1":
                    return new SHA1Managed();
                case "SHA256":
                case "SHA-256":
                case "System.Security.Cryptography.SHA256":
                    return new SHA256Managed();
                case "SHA384":
                case "SHA-384":
                case "System.Security.Cryptography.SHA384":
                    return new SHA384Managed();
                case "SHA512":
                case "SHA-512":
                case "System.Security.Cryptography.SHA512":
                    return new SHA512Managed();
#pragma warning restore SYSLIB0021

                case "System.Security.Cryptography.HMAC":
                case "HMACSHA1":
                case "System.Security.Cryptography.HMACSHA1":
                case "System.Security.Cryptography.KeyedHashAlgorithm":
                    return new HMACSHA1();
                case "HMACSHA256":
                case "System.Security.Cryptography.HMACSHA256":
                    return new HMACSHA256();
                case "HMACSHA384":
                case "System.Security.Cryptography.HMACSHA384":
                    return new HMACSHA384();
                case "HMACSHA512":
                case "System.Security.Cryptography.HMACSHA512":
                    return new HMACSHA512();

                case "System.Security.Cryptography.RSAPKCS1SignatureFormatter":
                    return new RSAPKCS1SignatureFormatter();
                case "System.Security.Cryptography.RSAPKCS1SignatureDeformatter":
                    return new RSAPKCS1SignatureDeformatter();
                case "System.Security.Cryptography.DSASignatureFormatter":
                    return new DSASignatureFormatter();
                case "System.Security.Cryptography.DSASignatureDeformatter":
                    return new DSASignatureDeformatter();

#pragma warning disable SYSLIB0023 // RNGCryptoServiceProvider is obsolete
                case "RandomNumberGenerator":
                case "System.Security.Cryptography.RandomNumberGenerator":
                    return new RNGCryptoServiceProvider();
#pragma warning restore SYSLIB0023
            }

            return null;
        }

        [RequiresUnreferencedCode(CreateFromNameUnreferencedCodeMessage)]
        public static object? CreateFromName(string name)
        {
            return CreateFromName(name, null);
        }

        [RequiresUnreferencedCode(CreateFromNameUnreferencedCodeMessage)]
        internal static T? CreateFromName<T>(string name) where T : class
        {
            object? o = CreateFromName(name);
            try
            {
                return (T?)o;
            }
            catch
            {
                (o as IDisposable)?.Dispose();
                throw;
            }
        }

        [UnsupportedOSPlatform("browser")]
        public static void AddOID(string oid, params string[] names)
        {
            ArgumentNullException.ThrowIfNull(oid);
            ArgumentNullException.ThrowIfNull(names);

            string[] oidNames = new string[names.Length];
            Array.Copy(names, oidNames, oidNames.Length);

            // Pre-check the input names for validity, so that we don't add a few of the names and throw an
            // exception if an invalid name is found further down the array.
            foreach (string name in oidNames)
            {
                ArgumentException.ThrowIfNullOrEmpty(name, nameof(names));
            }

            // Everything is valid, so we're good to lock the hash table and add the application mappings
            foreach (string name in oidNames)
            {
                appOidHT[name] = oid;
            }
        }

        [UnsupportedOSPlatform("browser")]
        public static string? MapNameToOID(string name)
        {
            ArgumentNullException.ThrowIfNull(name);

            appOidHT.TryGetValue(name, out string? oidName);

            if (string.IsNullOrEmpty(oidName) && !DefaultOidHT.TryGetValue(name, out oidName))
            {
                try
                {
                    Oid oid = Oid.FromFriendlyName(name, OidGroup.All);
                    oidName = oid.Value;
                }
                catch (CryptographicException) { }
            }

            return oidName;
        }

        [UnsupportedOSPlatform("browser")]
        [Obsolete(Obsoletions.CryptoConfigEncodeOIDMessage, DiagnosticId = Obsoletions.CryptoConfigEncodeOIDDiagId, UrlFormat = Obsoletions.SharedUrlFormat)]
        public static byte[] EncodeOID(string str)
        {
            ArgumentNullException.ThrowIfNull(str);

            string[] oidString = str.Split('.'); // valid ASN.1 separator
            uint[] oidNums = new uint[oidString.Length];
            for (int i = 0; i < oidString.Length; i++)
            {
                oidNums[i] = unchecked((uint)int.Parse(oidString[i], CultureInfo.InvariantCulture));
            }

            // Handle the first two oidNums special
            if (oidNums.Length < 2)
                throw new CryptographicUnexpectedOperationException(SR.Cryptography_InvalidOID);

            uint firstTwoOidNums = unchecked((oidNums[0] * 40) + oidNums[1]);

            // Determine length of output array
            int encodedOidNumsLength = 2; // Reserve first two bytes for later
            EncodeSingleOidNum(firstTwoOidNums, null, ref encodedOidNumsLength);
            for (int i = 2; i < oidNums.Length; i++)
            {
                EncodeSingleOidNum(oidNums[i], null, ref encodedOidNumsLength);
            }

            // Allocate the array to receive encoded oidNums
            byte[] encodedOidNums = new byte[encodedOidNumsLength];
            int encodedOidNumsIndex = 2;

            // Encode each segment
            EncodeSingleOidNum(firstTwoOidNums, encodedOidNums, ref encodedOidNumsIndex);
            for (int i = 2; i < oidNums.Length; i++)
            {
                EncodeSingleOidNum(oidNums[i], encodedOidNums, ref encodedOidNumsIndex);
            }

            Debug.Assert(encodedOidNumsIndex == encodedOidNumsLength);

            // Final return value is 06 <length> encodedOidNums[]
            if (encodedOidNumsIndex - 2 > 0x7f)
                throw new CryptographicUnexpectedOperationException(SR.Cryptography_Config_EncodedOIDError);

            encodedOidNums[0] = (byte)0x06;
            encodedOidNums[1] = (byte)(encodedOidNumsIndex - 2);

            return encodedOidNums;
        }

        private static void EncodeSingleOidNum(uint value, byte[]? destination, ref int index)
        {
            // Write directly to destination starting at index, and update index based on how many bytes written.
            // If destination is null, just return updated index.
            if (unchecked((int)value) < 0x80)
            {
                if (destination != null)
                {
                    destination[index++] = unchecked((byte)value);
                }
                else
                {
                    index += 1;
                }
            }
            else if (value < 0x4000)
            {
                if (destination != null)
                {
                    destination[index++] = (byte)((value >> 7) | 0x80);
                    destination[index++] = (byte)(value & 0x7f);
                }
                else
                {
                    index += 2;
                }
            }
            else if (value < 0x200000)
            {
                if (destination != null)
                {
                    unchecked
                    {
                        destination[index++] = (byte)((value >> 14) | 0x80);
                        destination[index++] = (byte)((value >> 7) | 0x80);
                        destination[index++] = (byte)(value & 0x7f);
                    }
                }
                else
                {
                    index += 3;
                }
            }
            else if (value < 0x10000000)
            {
                if (destination != null)
                {
                    unchecked
                    {
                        destination[index++] = (byte)((value >> 21) | 0x80);
                        destination[index++] = (byte)((value >> 14) | 0x80);
                        destination[index++] = (byte)((value >> 7) | 0x80);
                        destination[index++] = (byte)(value & 0x7f);
                    }
                }
                else
                {
                    index += 4;
                }
            }
            else
            {
                if (destination != null)
                {
                    unchecked
                    {
                        destination[index++] = (byte)((value >> 28) | 0x80);
                        destination[index++] = (byte)((value >> 21) | 0x80);
                        destination[index++] = (byte)((value >> 14) | 0x80);
                        destination[index++] = (byte)((value >> 7) | 0x80);
                        destination[index++] = (byte)(value & 0x7f);
                    }
                }
                else
                {
                    index += 5;
                }
            }
        }
    }
}
