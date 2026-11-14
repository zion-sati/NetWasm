using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace NetWasm.System.Security.Cryptography.Tests;

// The observations the suite compares with desktop .NET 11 on Linux. Deterministic outputs (ciphertexts, PKCS#1 v1.5
// signatures, key encodings, certificates) are recorded as length and FNV-1a hash, so a matching fingerprint means the
// same bytes as desktop's OpenSSL produced. Randomized outputs (PSS, OAEP, ECDSA, DSA, encrypted keys, PFX) are checked
// by round trip and by reading what desktop produced (CryptographyVectors).
internal static class CryptographyCases
{
    internal static readonly byte[] Message = Data(300, 7);

    internal static readonly DateTimeOffset NotBefore = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static readonly DateTimeOffset NotAfter = new(2034, 1, 1, 0, 0, 0, TimeSpan.Zero);

    internal static readonly DateTime ChainTime = new(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    internal static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    internal static string Print(ReadOnlySpan<byte> bytes)
    {
        ulong hash = 14695981039346656037;
        foreach (byte value in bytes)
        {
            hash ^= value;
            hash *= 1099511628211;
        }

        return bytes.Length + ":" + hash.ToString("X16");
    }

    internal static byte[] Data(int length, uint seed)
    {
        var data = new byte[length];
        uint state = seed;
        for (int i = 0; i < data.Length; i++)
        {
            state = state * 1103515245 + 12345;
            data[i] = (byte)(state >> 16);
        }

        return data;
    }

    // Exception kinds by type test, not by name.
    internal static string Failure(Action action)
    {
        try
        {
            action();
            return "none";
        }
        catch (AuthenticationTagMismatchException)
        {
            return "AuthenticationTagMismatchException";
        }
        catch (CryptographicException)
        {
            return "CryptographicException";
        }
        catch (ArgumentNullException)
        {
            return "ArgumentNullException";
        }
        catch (ArgumentOutOfRangeException)
        {
            return "ArgumentOutOfRangeException";
        }
        catch (ArgumentException)
        {
            return "ArgumentException";
        }
        catch (PlatformNotSupportedException)
        {
            return "PlatformNotSupportedException";
        }
        catch (NotSupportedException)
        {
            return "NotSupportedException";
        }
    }

    internal static IEnumerable<string> KnownAnswers()
    {
        using (Aes aes = Aes.Create())
        {
            // FIPS-197, appendix C.1 and C.3.
            aes.Key = Convert.FromHexString("000102030405060708090A0B0C0D0E0F");
            yield return "aes-128 " + Hex(aes.EncryptEcb(Convert.FromHexString("00112233445566778899AABBCCDDEEFF"), PaddingMode.None));
            aes.Key = Convert.FromHexString("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F");
            yield return "aes-256 " + Hex(aes.EncryptEcb(Convert.FromHexString("00112233445566778899AABBCCDDEEFF"), PaddingMode.None));
            yield return "aes-256 decrypt " + Hex(aes.DecryptEcb(Convert.FromHexString("8EA2B7CA516745BFEAFC49904B496089"), PaddingMode.None));
        }

        using (TripleDES des3 = TripleDES.Create())
        {
            des3.Key = Convert.FromHexString("0123456789ABCDEF23456789ABCDEF01456789ABCDEF0123");
            yield return "3des " + Hex(des3.EncryptEcb(Encoding.ASCII.GetBytes("The qufck brown fox jump"), PaddingMode.None));
        }

        using (DES des = DES.Create())
        {
            des.Key = Convert.FromHexString("133457799BBCDFF1");
            yield return "des " + Hex(des.EncryptEcb(Convert.FromHexString("0123456789ABCDEF"), PaddingMode.None));
        }

        using (RC2 rc2 = RC2.Create())
        {
            // RFC 2268, section 5: a 64-bit key with 64 effective bits.
            rc2.Key = Convert.FromHexString("FFFFFFFFFFFFFFFF");
            rc2.EffectiveKeySize = 64;
            yield return "rc2 " + Hex(rc2.EncryptEcb(Convert.FromHexString("FFFFFFFFFFFFFFFF"), PaddingMode.None));
        }

        // NIST GCM specification, test case 3.
        using (var gcm = new AesGcm(Convert.FromHexString("FEFFE9928665731C6D6A8F9467308308"), 16))
        {
            byte[] plaintext = Convert.FromHexString(
                "D9313225F88406E5A55909C5AFF5269A86A7A9531534F7DA2E4C303D8A318A721C3C0C95956809532FCF0E2449A6B525B16AEDF5AA0DE657BA637B391AAFD255");
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            gcm.Encrypt(Convert.FromHexString("CAFEBABEFACEDBADDECAF888"), plaintext, ciphertext, tag);
            yield return "gcm " + Hex(ciphertext.AsSpan(0, 16)) + " " + Hex(tag);
        }

        // RFC 8439, section 2.8.2.
        using (var chacha = new ChaCha20Poly1305(Convert.FromHexString("808182838485868788898A8B8C8D8E8F909192939495969798999A9B9C9D9E9F")))
        {
            byte[] plaintext = Encoding.ASCII.GetBytes(
                "Ladies and Gentlemen of the class of '99: If I could offer you only one tip for the future, sunscreen would be it.");
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            chacha.Encrypt(Convert.FromHexString("070000004041424344454647"), plaintext, ciphertext, tag, Convert.FromHexString("50515253C0C1C2C3C4C5C6C7"));
            yield return "chacha20-poly1305 " + Hex(ciphertext.AsSpan(0, 16)) + " " + Hex(tag);
        }
    }

    internal static IEnumerable<string> BlockCiphers()
    {
        byte[] data = Data(1000, 11);
        foreach ((string name, Func<SymmetricAlgorithm> create, int[] keySizes) in new (string, Func<SymmetricAlgorithm>, int[])[]
        {
            ("aes", () => Aes.Create(), [128, 192, 256]),
            ("3des", () => TripleDES.Create(), [128, 192]),
            ("des", () => DES.Create(), [64]),
            ("rc2", () => RC2.Create(), [40, 128]),
        })
        {
            foreach (int keySize in keySizes)
            {
                using SymmetricAlgorithm cipher = create();
                cipher.Key = Data(keySize / 8, (uint)keySize);
                byte[] iv = Data(cipher.BlockSize / 8, 3);
                foreach (PaddingMode padding in new[] { PaddingMode.PKCS7, PaddingMode.Zeros, PaddingMode.ANSIX923 })
                {
                    yield return $"{name}-{keySize} ecb {padding} " + Observe(() => cipher.EncryptEcb(data, padding), ciphertext => cipher.DecryptEcb(ciphertext, padding));
                    yield return $"{name}-{keySize} cbc {padding} " + Observe(() => cipher.EncryptCbc(data, iv, padding), ciphertext => cipher.DecryptCbc(ciphertext, iv, padding));
                    yield return $"{name}-{keySize} cfb8 {padding} " + Observe(() => cipher.EncryptCfb(data, iv, padding, 8), ciphertext => cipher.DecryptCfb(ciphertext, iv, padding, 8));
                }

                yield return $"{name}-{keySize} cfb{cipher.BlockSize} " + Observe(
                    () => cipher.EncryptCfb(data, iv, PaddingMode.PKCS7, cipher.BlockSize),
                    ciphertext => cipher.DecryptCfb(ciphertext, iv, PaddingMode.PKCS7, cipher.BlockSize));

                // The transforms and CryptoStream produce what the one-shot methods do.
                yield return $"{name}-{keySize} stream " + Observe(() => Stream(cipher, data, iv));
            }
        }

        using (Aes aes = Aes.Create())
        {
            aes.Key = Data(16, 5);
            byte[] iso = aes.EncryptCbc(data, new byte[16], PaddingMode.ISO10126);
            yield return "iso10126 " + iso.Length + " " + aes.DecryptCbc(iso, new byte[16], PaddingMode.ISO10126).AsSpan().SequenceEqual(data);
            yield return "partial block " + Failure(() => aes.EncryptEcb(new byte[15], PaddingMode.None));
            yield return "bad padding " + Failure(() => aes.DecryptCbc(Data(32, 9), new byte[16], PaddingMode.PKCS7));
            yield return "iv size " + Failure(() => aes.EncryptCbc(data, new byte[8]));
        }

        using (DES des = DES.Create())
        {
            yield return "weak key " + DES.IsWeakKey(Convert.FromHexString("0101010101010101")) + " " + Failure(() => des.Key = Convert.FromHexString("0101010101010101"));
        }
    }

    private static string Stream(SymmetricAlgorithm cipher, byte[] data, byte[] iv)
    {
        cipher.Mode = CipherMode.CBC;
        cipher.Padding = PaddingMode.PKCS7;
        cipher.IV = iv;
        using var buffer = new MemoryStream();
        using (var stream = new CryptoStream(buffer, cipher.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
        {
            stream.Write(data, 0, 333);
            stream.Write(data, 333, data.Length - 333);
        }

        byte[] streamed = buffer.ToArray();
        using var decrypted = new MemoryStream();
        using (var stream = new CryptoStream(new MemoryStream(streamed), cipher.CreateDecryptor(), CryptoStreamMode.Read))
        {
            stream.CopyTo(decrypted);
        }

        return streamed.AsSpan().SequenceEqual(cipher.EncryptCbc(data, iv)) + " " + decrypted.ToArray().AsSpan().SequenceEqual(data);
    }

    private static string Observe(Func<byte[]> encrypt, Func<byte[], byte[]> decrypt)
    {
        byte[] ciphertext;
        try
        {
            ciphertext = encrypt();
        }
        catch (Exception exception)
        {
            return Failure(() => throw exception);
        }

        byte[] plaintext = decrypt(ciphertext);
        return Print(ciphertext) + " " + Print(plaintext);
    }

    internal static IEnumerable<string> Aead()
    {
        byte[] key = Data(32, 21);
        byte[] plaintext = Data(777, 22);
        byte[] associated = Data(40, 23);

        using (var gcm = new AesGcm(key, 16))
        {
            foreach (int tagSize in new[] { 12, 13, 14, 15, 16 })
            {
                using var sized = new AesGcm(key, tagSize);
                byte[] ciphertext = new byte[plaintext.Length];
                byte[] tag = new byte[tagSize];
                sized.Encrypt(Data(12, 24), plaintext, ciphertext, tag, associated);
                byte[] back = new byte[plaintext.Length];
                sized.Decrypt(Data(12, 24), ciphertext, tag, back, associated);
                yield return $"gcm tag {tagSize} {Print(ciphertext)} {Hex(tag)} {back.AsSpan().SequenceEqual(plaintext)}";
            }

            byte[] sealedText = new byte[plaintext.Length];
            byte[] sealedTag = new byte[16];
            gcm.Encrypt(Data(12, 25), plaintext, sealedText, sealedTag);
            sealedTag[0] ^= 1;
            byte[] opened = Data(plaintext.Length, 26);
            yield return "gcm tampered " + Failure(() => gcm.Decrypt(Data(12, 25), sealedText, sealedTag, opened)) + " " + opened.All(b => b == 0);
            yield return "gcm nonce " + Failure(() => gcm.Encrypt(new byte[11], plaintext, sealedText, new byte[16]));
            yield return "gcm sizes " + AesGcm.NonceByteSizes.MinSize + " " + AesGcm.TagByteSizes.MinSize + " " + AesGcm.TagByteSizes.MaxSize;
        }

        foreach (int nonceSize in new[] { 7, 13 })
        {
            foreach (int tagSize in new[] { 4, 16 })
            {
                using var ccm = new AesCcm(key.AsSpan(0, 16));
                byte[] ciphertext = new byte[plaintext.Length];
                byte[] tag = new byte[tagSize];
                ccm.Encrypt(Data(nonceSize, 27), plaintext, ciphertext, tag, associated);
                byte[] back = new byte[plaintext.Length];
                ccm.Decrypt(Data(nonceSize, 27), ciphertext, tag, back, associated);
                yield return $"ccm {nonceSize} {tagSize} {Print(ciphertext)} {Hex(tag)} {back.AsSpan().SequenceEqual(plaintext)}";
            }
        }

        using (var ccm = new AesCcm(key.AsSpan(0, 16)))
        {
            yield return "ccm tag " + Failure(() => ccm.Encrypt(Data(12, 27), plaintext, new byte[plaintext.Length], new byte[5]));
        }

        using (var chacha = new ChaCha20Poly1305(key))
        {
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            chacha.Encrypt(Data(12, 28), plaintext, ciphertext, tag, associated);
            byte[] back = new byte[plaintext.Length];
            chacha.Decrypt(Data(12, 28), ciphertext, tag, back, associated);
            yield return $"chacha {Print(ciphertext)} {Hex(tag)} {back.AsSpan().SequenceEqual(plaintext)}";
            yield return "chacha tampered " + Failure(() => chacha.Decrypt(Data(12, 28), ciphertext, tag, back, Data(40, 29)));
        }

        yield return "supported " + AesGcm.IsSupported + " " + AesCcm.IsSupported + " " + ChaCha20Poly1305.IsSupported;
    }

    internal static IEnumerable<string> Rsa()
    {
        using RSA rsa = RSA.Create();
        rsa.ImportFromPem(CryptographyKeys.Rsa);
        yield return "size " + rsa.KeySize;
        yield return "rsa public " + Print(rsa.ExportRSAPublicKey());
        yield return "rsa private " + Print(rsa.ExportRSAPrivateKey());
        yield return "spki " + Print(rsa.ExportSubjectPublicKeyInfo());
        yield return "pkcs8 " + Print(rsa.ExportPkcs8PrivateKey());
        yield return "pem " + rsa.ExportSubjectPublicKeyInfoPem().Split('\n')[0];
        RSAParameters parameters = rsa.ExportParameters(true);
        yield return "parameters " + Print(parameters.Modulus) + " " + Hex(parameters.Exponent) + " " + parameters.D!.Length + " " + parameters.InverseQ!.Length;

        foreach (HashAlgorithmName hash in new[] { HashAlgorithmName.SHA1, HashAlgorithmName.SHA256, HashAlgorithmName.SHA384, HashAlgorithmName.SHA512 })
        {
            byte[] signature = rsa.SignData(Message, hash, RSASignaturePadding.Pkcs1);
            yield return $"pkcs1 {hash.Name} {Print(signature)} {rsa.VerifyData(Message, signature, hash, RSASignaturePadding.Pkcs1)}";
            byte[] pss = rsa.SignData(Message, hash, RSASignaturePadding.Pss);
            yield return $"pss {hash.Name} {pss.Length} {rsa.VerifyData(Message, pss, hash, RSASignaturePadding.Pss)}";
        }

        byte[] desktopPss = Convert.FromBase64String(CryptographyVectors.RsaPssSha256);
        yield return "desktop pss " + rsa.VerifyData(Message, desktopPss, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        desktopPss[10] ^= 1;
        yield return "tampered pss " + rsa.VerifyData(Message, desktopPss, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        byte[] hash256 = SHA256.HashData(Message);
        yield return "sign hash " + Print(rsa.SignHash(hash256, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        byte[] secret = Encoding.UTF8.GetBytes("netwasm oaep");
        foreach (RSAEncryptionPadding padding in new[] { RSAEncryptionPadding.Pkcs1, RSAEncryptionPadding.OaepSHA1, RSAEncryptionPadding.OaepSHA256, RSAEncryptionPadding.OaepSHA512 })
        {
            byte[] encrypted = rsa.Encrypt(secret, padding);
            yield return $"encrypt {padding} {encrypted.Length} {Encoding.UTF8.GetString(rsa.Decrypt(encrypted, padding))}";
        }

        yield return "desktop oaep " + Encoding.UTF8.GetString(rsa.Decrypt(Convert.FromBase64String(CryptographyVectors.RsaOaepSha256), RSAEncryptionPadding.OaepSHA256));
        yield return "decrypt garbage " + Failure(() => rsa.Decrypt(Data(256, 31), RSAEncryptionPadding.OaepSHA256));
        yield return "too long " + Failure(() => rsa.Encrypt(new byte[300], RSAEncryptionPadding.Pkcs1));

        // Encrypted PKCS#8: desktop's file reads back, and each PBE scheme round-trips.
        using (RSA imported = RSA.Create())
        {
            imported.ImportFromEncryptedPem(CryptographyVectors.RsaEncryptedPkcs8, "pw");
            yield return "desktop encrypted pkcs8 " + Print(imported.ExportPkcs8PrivateKey());
            yield return "wrong password " + Failure(() => RSA.Create().ImportFromEncryptedPem(CryptographyVectors.RsaEncryptedPkcs8, "nope"));
        }

        foreach (PbeParameters pbe in new[]
        {
            new PbeParameters(PbeEncryptionAlgorithm.Aes128Cbc, HashAlgorithmName.SHA256, 1000),
            new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA512, 10),
            new PbeParameters(PbeEncryptionAlgorithm.TripleDes3KeyPkcs12, HashAlgorithmName.SHA1, 2000),
        })
        {
            byte[] encrypted = rsa.ExportEncryptedPkcs8PrivateKey("pw", pbe);
            using RSA back = RSA.Create();
            back.ImportEncryptedPkcs8PrivateKey("pw", encrypted, out int read);
            yield return $"encrypted pkcs8 {pbe.EncryptionAlgorithm} {read == encrypted.Length} {Print(back.ExportPkcs8PrivateKey())}";
        }

        // Generation draws on OpenSSL's generator, which NetWasm seeds from the platform's random source.
        using (RSA first = RSA.Create(2048))
        using (RSA second = RSA.Create(2048))
        {
            byte[] signature = first.SignData(Message, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
            yield return "generated " + first.KeySize + " " + first.VerifyData(Message, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss) +
                " " + !first.ExportParameters(false).Modulus!.AsSpan().SequenceEqual(second.ExportParameters(false).Modulus);
        }

        using (var csp = new RSACryptoServiceProvider())
        {
            csp.ImportParameters(parameters);
            yield return "csp " + csp.KeySize + " " + Print(csp.ExportCspBlob(false)) + " " +
                csp.SignData(Message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).AsSpan().SequenceEqual(rsa.SignData(Message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) +
                " " + Encoding.UTF8.GetString(csp.Decrypt(csp.Encrypt(secret, RSAEncryptionPadding.Pkcs1), RSAEncryptionPadding.Pkcs1));
        }

        using (var openSsl = new RSAOpenSsl(rsa.ExportParameters(false)))
        {
            yield return "openssl verify " + openSsl.VerifyData(Message, rsa.SignData(Message, HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1), HashAlgorithmName.SHA384, RSASignaturePadding.Pkcs1);
        }
    }

    internal static IEnumerable<string> EllipticCurves()
    {
        using ECDsa ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(CryptographyKeys.EcDsa);
        yield return "size " + ecdsa.KeySize;
        yield return "ec private " + Print(ecdsa.ExportECPrivateKey());
        yield return "spki " + Print(ecdsa.ExportSubjectPublicKeyInfo());
        ECParameters explicitParameters = ecdsa.ExportExplicitParameters(false);
        yield return "explicit " + Print(explicitParameters.Curve.Prime) + " " + Print(explicitParameters.Curve.Order) + " " + Hex(explicitParameters.Curve.Cofactor);
        yield return "named " + ecdsa.ExportParameters(false).Curve.Oid.Value;

        byte[] desktop = Convert.FromBase64String(CryptographyVectors.EcDsaP1363Sha256);
        yield return "desktop p1363 " + ecdsa.VerifyData(Message, desktop, HashAlgorithmName.SHA256);
        byte[] desktopDer = Convert.FromBase64String(CryptographyVectors.EcDsaDerSha256);
        yield return "desktop der " + ecdsa.VerifyData(Message, desktopDer, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        desktop[5] ^= 1;
        yield return "tampered " + ecdsa.VerifyData(Message, desktop, HashAlgorithmName.SHA256);
        byte[] signature = ecdsa.SignData(Message, HashAlgorithmName.SHA384);
        yield return "sign " + signature.Length + " " + ecdsa.VerifyData(Message, signature, HashAlgorithmName.SHA384);
        yield return "sign hash " + ecdsa.VerifyHash(SHA256.HashData(Message), ecdsa.SignHash(SHA256.HashData(Message)));

        foreach (string curve in new[] { "nistP256", "nistP384", "nistP521", "brainpoolP256r1", "brainpoolP384r1", "brainpoolP512r1", "secP256k1" })
        {
            yield return "curve " + curve + " " + Observe(() =>
            {
                using ECDsa key = ECDsa.Create(ECCurve.CreateFromFriendlyName(curve));
                return key.KeySize + " " + key.VerifyData(Message, key.SignData(Message, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256);
            });
        }

        using ECDiffieHellman first = ECDiffieHellman.Create();
        first.ImportFromPem(CryptographyKeys.EcDh);
        using ECDiffieHellman second = ECDiffieHellman.Create();
        second.ImportFromPem(CryptographyKeys.EcDsa);
        byte[] forward = first.DeriveKeyFromHash(second.PublicKey, HashAlgorithmName.SHA256);
        byte[] backward = second.DeriveKeyFromHash(first.PublicKey, HashAlgorithmName.SHA256);
        yield return "ecdh hash " + Hex(forward) + " " + forward.AsSpan().SequenceEqual(backward);
        yield return "ecdh hmac " + Hex(first.DeriveKeyFromHmac(second.PublicKey, HashAlgorithmName.SHA384, Data(16, 41), Data(4, 42), Data(4, 43)));
        yield return "ecdh tls " + Observe(() => Hex(first.DeriveKeyTls(second.PublicKey, Data(13, 44), Data(64, 45))));
        yield return "ecdh material " + Hex(first.DeriveKeyMaterial(second.PublicKey));
        using ECDiffieHellman other = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        yield return "ecdh curves " + Failure(() => first.DeriveKeyFromHash(other.PublicKey, HashAlgorithmName.SHA256));
    }

    private static string Observe(Func<string> action)
    {
        try
        {
            return action();
        }
        catch (Exception exception)
        {
            return Failure(() => throw exception);
        }
    }

    internal static IEnumerable<string> Dsa()
    {
        using DSA dsa = DSA.Create();
        dsa.ImportFromPem(CryptographyKeys.Dsa);
        yield return "size " + dsa.KeySize;
        yield return "spki " + Print(dsa.ExportSubjectPublicKeyInfo());
        yield return "pkcs8 " + Print(dsa.ExportPkcs8PrivateKey());
        DSAParameters parameters = dsa.ExportParameters(true);
        yield return "parameters " + Print(parameters.P) + " " + Print(parameters.Q) + " " + parameters.X!.Length;
        yield return "desktop " + dsa.VerifyData(Message, Convert.FromBase64String(CryptographyVectors.DsaSha256), HashAlgorithmName.SHA256);
        byte[] signature = dsa.SignData(Message, HashAlgorithmName.SHA256);
        yield return "sign " + signature.Length + " " + dsa.VerifyData(Message, signature, HashAlgorithmName.SHA256);
        yield return "der " + dsa.VerifyData(Message, dsa.SignData(Message, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        using (DSA generated = DSA.Create(1024))
        {
            yield return "generated " + generated.KeySize + " " + generated.VerifyData(Message, generated.SignData(Message, HashAlgorithmName.SHA1), HashAlgorithmName.SHA1);
        }

        using (var csp = new DSACryptoServiceProvider())
        {
            using DSA small = DSA.Create(1024);
            csp.ImportParameters(small.ExportParameters(true));
            byte[] cspSignature = csp.SignData(Message);
            yield return "csp " + csp.KeySize + " " + cspSignature.Length + " " + small.VerifyData(Message, cspSignature, HashAlgorithmName.SHA1) + " " + csp.ExportCspBlob(false).Length;
        }
    }

    internal static X509Certificate2 CreateRoot(RSA key)
    {
        var request = new CertificateRequest("CN=NetWasm Test Root, O=NetWasm", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 1, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using X509Certificate2 certificate = request.Create(request.SubjectName, X509SignatureGenerator.CreateForRSA(key, RSASignaturePadding.Pkcs1),
            NotBefore, NotAfter, [1]);
        return certificate.CopyWithPrivateKey(key);
    }

    internal static X509Certificate2 CreateLeaf(X509Certificate2 root, RSA key)
    {
        var request = new CertificateRequest("CN=example.test, O=NetWasm", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("example.test");
        names.AddIpAddress(IPAddress.Parse("192.0.2.1"));
        names.AddEmailAddress("ops@example.test");
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2") }, false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.Create(root, NotBefore.AddDays(1), NotAfter.AddDays(-1), [2, 3]);
    }

    internal static IEnumerable<string> Certificates()
    {
        using RSA rootKey = RSA.Create();
        rootKey.ImportFromPem(CryptographyKeys.Rsa);
        using RSA leafKey = RSA.Create();
        leafKey.ImportFromPem(CryptographyKeys.OtherRsa);
        using X509Certificate2 root = CreateRoot(rootKey);
        using X509Certificate2 leaf = CreateLeaf(root, leafKey);

        foreach ((string label, X509Certificate2 certificate) in new[] { ("root", root), ("leaf", leaf) })
        {
            yield return $"{label} {Print(certificate.RawData)} {certificate.Thumbprint}";
            yield return $"{label} {certificate.Subject} | {certificate.Issuer} | {certificate.SerialNumber} | v{certificate.Version}";
            yield return $"{label} {certificate.NotBefore.ToUniversalTime():O} {certificate.NotAfter.ToUniversalTime():O} {certificate.HasPrivateKey}";
            yield return $"{label} {certificate.SignatureAlgorithm.Value} {certificate.SignatureAlgorithm.FriendlyName} {certificate.PublicKey.Oid.FriendlyName} {certificate.GetKeyAlgorithm()}";
            yield return $"{label} {certificate.GetNameInfo(X509NameType.SimpleName, false)} | {certificate.GetNameInfo(X509NameType.DnsName, false)} | {certificate.GetNameInfo(X509NameType.SimpleName, true)}";
            foreach (X509Extension extension in certificate.Extensions)
            {
                yield return $"{label} {extension.Oid!.Value} {extension.Critical} {extension.Format(false)}";
            }
        }

        yield return "subject multiline " + leaf.SubjectName.Format(true).Replace(Environment.NewLine, "/");
        yield return "subject reversed " + leaf.SubjectName.Decode(X500DistinguishedNameFlags.Reversed | X500DistinguishedNameFlags.UseSemicolons);
        yield return "leaf key " + leaf.GetRSAPublicKey()!.VerifyData(Message, leafKey.SignData(Message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        yield return "root key " + root.GetRSAPrivateKey()!.SignData(Message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).AsSpan().SequenceEqual(rootKey.SignData(Message, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        yield return "ecdsa key " + (leaf.GetECDsaPublicKey() is null);

        // DER and PEM read back.
        using (var der = new X509Certificate2(leaf.RawData))
        {
            yield return "der " + der.Thumbprint + " " + der.HasPrivateKey;
        }

        string base64 = Convert.ToBase64String(leaf.RawData);
        string pem = "-----BEGIN CERTIFICATE-----\n" +
            string.Join("\n", Enumerable.Range(0, (base64.Length + 63) / 64).Select(line => base64.Substring(line * 64, Math.Min(64, base64.Length - line * 64)))) +
            "\n-----END CERTIFICATE-----\n";
        using (var fromPem = new X509Certificate2(Encoding.ASCII.GetBytes(pem)))
        {
            yield return "pem " + fromPem.Thumbprint;
        }

        yield return "content " + X509Certificate2.GetCertContentType(leaf.RawData) + " " + X509Certificate2.GetCertContentType(root.Export(X509ContentType.Pfx, "pw"));
        yield return "garbage " + Failure(() => new X509Certificate2(Data(100, 51)));

        // PKCS#12: this run's export reads back, and so does desktop's.
        byte[] pfx = root.Export(X509ContentType.Pfx, "pw");
        using (var back = new X509Certificate2(pfx, "pw", X509KeyStorageFlags.Exportable))
        {
            yield return "pfx " + back.Thumbprint + " " + back.HasPrivateKey + " " + Print(back.GetRSAPrivateKey()!.ExportPkcs8PrivateKey());
        }

        using (var desktop = new X509Certificate2(Convert.FromBase64String(CryptographyVectors.RootPfx), "pw", X509KeyStorageFlags.Exportable))
        {
            yield return "desktop pfx " + desktop.Thumbprint + " " + desktop.HasPrivateKey + " " + Print(desktop.GetRSAPrivateKey()!.ExportPkcs8PrivateKey());
        }

        yield return "pfx password " + Failure(() => new X509Certificate2(pfx, "wrong"));
        var collection = new X509Certificate2Collection();
        collection.Import(pfx, "pw", X509KeyStorageFlags.DefaultKeySet);
        yield return "pfx collection " + collection.Count + " " + collection[0].Thumbprint;

        var pair = new X509Certificate2Collection { root, leaf };
        byte[] pkcs7 = pair.Export(X509ContentType.Pkcs7)!;
        var fromPkcs7 = new X509Certificate2Collection();
        fromPkcs7.Import(pkcs7);
        yield return "pkcs7 " + X509Certificate2.GetCertContentType(pkcs7) + " " + string.Join(",", fromPkcs7.Cast<X509Certificate2>().Select(c => c.Thumbprint).Order());
        yield return "find " + pair.Find(X509FindType.FindByThumbprint, leaf.Thumbprint, false).Count + " " + pair.Find(X509FindType.FindBySubjectName, "example.test", false).Count +
            " " + pair.Find(X509FindType.FindByExtension, "2.5.29.17", false).Count;
    }

    // Certificates read from files, through the managed file APIs on NetWasm (native code has no file system).
    internal static IEnumerable<string> Files()
    {
        using RSA rootKey = RSA.Create();
        rootKey.ImportFromPem(CryptographyKeys.Rsa);
        using RSA leafKey = RSA.Create();
        leafKey.ImportFromPem(CryptographyKeys.OtherRsa);
        using X509Certificate2 root = CreateRoot(rootKey);
        using X509Certificate2 leaf = CreateLeaf(root, leafKey);
        string directory = Path.Combine(Path.GetTempPath(), "netwasm-crypto-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string der = Path.Combine(directory, "leaf.cer");
            File.WriteAllBytes(der, leaf.RawData);
            string pem = Path.Combine(directory, "leaf.pem");
            File.WriteAllText(pem, "-----BEGIN CERTIFICATE-----\n" + Convert.ToBase64String(leaf.RawData) + "\n-----END CERTIFICATE-----\n");
            string pfx = Path.Combine(directory, "root.pfx");
            File.WriteAllBytes(pfx, root.Export(X509ContentType.Pfx, "pw"));

            using (var fromDer = new X509Certificate2(der))
            {
                yield return "der file " + fromDer.Thumbprint;
            }

            using (var fromPem = new X509Certificate2(pem))
            {
                yield return "pem file " + fromPem.Thumbprint;
            }

            using (var fromPfx = new X509Certificate2(pfx, "pw"))
            {
                yield return "pfx file " + fromPfx.Thumbprint + " " + fromPfx.HasPrivateKey;
            }

            yield return "content " + X509Certificate2.GetCertContentType(der) + " " + X509Certificate2.GetCertContentType(pfx);
            var collection = new X509Certificate2Collection();
            collection.Import(pfx, "pw", X509KeyStorageFlags.DefaultKeySet);
            yield return "collection " + collection.Count + " " + collection[0].Thumbprint;
            yield return "legacy " + X509Certificate.CreateFromCertFile(der).GetCertHashString();
            yield return "missing " + Failure(() => new X509Certificate2(Path.Combine(directory, "missing.cer")));
            yield return "wrong password " + Failure(() => new X509Certificate2(pfx, "wrong"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    internal static IEnumerable<string> Chains()
    {
        using RSA rootKey = RSA.Create();
        rootKey.ImportFromPem(CryptographyKeys.Rsa);
        using RSA leafKey = RSA.Create();
        leafKey.ImportFromPem(CryptographyKeys.OtherRsa);
        using X509Certificate2 root = CreateRoot(rootKey);
        using X509Certificate2 leaf = CreateLeaf(root, leafKey);

        yield return "trusted " + Build(leaf, chain => chain.ChainPolicy.CustomTrustStore.Add(root));
        yield return "untrusted root " + Build(leaf, chain => chain.ChainPolicy.ExtraStore.Add(root));
        yield return "no issuer " + Build(leaf, chain => { });
        yield return "expired " + Build(leaf, chain =>
        {
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.VerificationTime = new DateTime(2040, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        });
        yield return "server usage " + Build(leaf, chain =>
        {
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
        });
        yield return "code signing usage " + Build(leaf, chain =>
        {
            chain.ChainPolicy.CustomTrustStore.Add(root);
            chain.ChainPolicy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.3"));
        });
        yield return "unknown authority allowed " + Build(leaf, chain =>
        {
            chain.ChainPolicy.ExtraStore.Add(root);
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        });
    }

    private static string Build(X509Certificate2 leaf, Action<X509Chain> configure)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationTime = ChainTime;
        configure(chain);
        bool built = chain.Build(leaf);
        X509ChainStatusFlags flags = chain.ChainStatus.Aggregate(X509ChainStatusFlags.NoError, (all, status) => all | status.Status);
        string elements = string.Join(",", chain.ChainElements.Cast<X509ChainElement>().Select(element =>
            element.Certificate.GetNameInfo(X509NameType.SimpleName, false) + ":" +
            element.ChainElementStatus.Aggregate(X509ChainStatusFlags.NoError, (all, status) => all | status.Status)));
        return $"{built} {flags} [{elements}]";
    }

    internal static IEnumerable<string> Formatting()
    {
        byte[] san = Convert.FromHexString("300D820B6578616D706C652E636F6D");
        foreach ((string oid, byte[] data) in new (string, byte[])[]
        {
            ("2.5.29.17", san),
            ("2.5.29.15", Convert.FromHexString("030205A0")),
            ("2.5.29.19", Convert.FromHexString("30060101FF020100")),
            ("2.5.29.37", Convert.FromHexString("301406082B0601050507030106082B06010505070302")),
            ("2.5.29.14", Convert.FromHexString("0414A1B2C3D4E5F60718293A4B5C6D7E8F9012345678")),
            ("1.2.3", Convert.FromHexString("010203")),
        })
        {
            var encoded = new AsnEncodedData(oid, data);
            yield return oid + " " + encoded.Format(false) + " | " + encoded.Format(true).Replace(Environment.NewLine, "/");
        }

        yield return "no oid " + new AsnEncodedData(Convert.FromHexString("AB01")).Format(true);
        yield return "oid names " + new Oid("1.2.840.113549.1.1.11").FriendlyName + " " + new Oid("2.5.4.3").FriendlyName + " " + Oid.FromFriendlyName("sha384", OidGroup.HashAlgorithm).Value;
    }
}
