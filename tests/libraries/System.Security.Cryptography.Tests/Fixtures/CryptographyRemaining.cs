// From the compiler harness's CryptographyRemainingCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
#pragma warning disable SYSLIB0021, SYSLIB0022, SYSLIB0045, CS0618
using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
namespace CryptoRemainingFixture;

// An explicit subclass exercises the base class's validation and one-shot dispatch.
sealed class XorAes : Aes
{
    public override ICryptoTransform CreateDecryptor(byte[] rgbKey, byte[]? rgbIV) => throw new NotSupportedException();
    public override ICryptoTransform CreateEncryptor(byte[] rgbKey, byte[]? rgbIV) => throw new NotSupportedException();
    public override void GenerateIV() => IVValue = new byte[BlockSize / 8];
    public override void GenerateKey() => KeyValue = new byte[KeySize / 8];

    protected override bool TryEncryptEcbCore(ReadOnlySpan<byte> plaintext, Span<byte> destination, PaddingMode paddingMode, out int bytesWritten)
    {
        for (int i = 0; i < plaintext.Length; i++) destination[i] = (byte)(plaintext[i] ^ 0x5A);
        bytesWritten = plaintext.Length;
        return true;
    }

    protected override bool TryDecryptEcbCore(ReadOnlySpan<byte> ciphertext, Span<byte> destination, PaddingMode paddingMode, out int bytesWritten) =>
        TryEncryptEcbCore(ciphertext, destination, paddingMode, out bytesWritten);
}

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    public static int Run(int input)
    {
        // SymmetricAlgorithm validation, cloning and legal sizes through an explicit subclass.
        using var aes = new XorAes();
        if (aes.KeySize != 256 || aes.BlockSize != 128 || aes.FeedbackSize != 8 || aes.Mode != CipherMode.CBC || aes.Padding != PaddingMode.PKCS7) return 1;
        aes.Key = new byte[16];
        if (aes.KeySize != 128 || !aes.ValidKeySize(192) || aes.ValidKeySize(100)) return 2;
        byte[] key = aes.Key;
        key[0] = 9;
        if (aes.Key[0] != 0) return 3;
        if (!Throws<CryptographicException>(() => aes.Key = new byte[15]) || !Throws<CryptographicException>(() => aes.IV = new byte[15])) return 4;
        if (!Throws<CryptographicException>(() => aes.Mode = (CipherMode)99) || !Throws<CryptographicException>(() => aes.Padding = (PaddingMode)99)) return 5;
        KeySizes[] legal = aes.LegalKeySizes;
        if (legal.Length != 1 || legal[0].MinSize != 128 || legal[0].MaxSize != 256 || legal[0].SkipSize != 64) return 6;
        if (aes.GetCiphertextLengthEcb(17, PaddingMode.PKCS7) != 32 || aes.GetCiphertextLengthCbc(16, PaddingMode.PKCS7) != 32 || aes.GetCiphertextLengthEcb(16, PaddingMode.None) != 16) return 7;
        byte[] plain = Encoding.ASCII.GetBytes("sixteen bytes!!!");
        byte[] cipher = aes.EncryptEcb(plain, PaddingMode.None);
        if (cipher.Length != 16 || cipher[0] != (byte)('s' ^ 0x5A) || Encoding.ASCII.GetString(aes.DecryptEcb(cipher, PaddingMode.None)) != "sixteen bytes!!!") return 8;
        if (!Throws<ArgumentException>(() => aes.EncryptEcb(new byte[15], PaddingMode.None))) return 9;

        // Weak-key checks are managed.
        if (!DES.IsWeakKey(new byte[] { 1, 1, 1, 1, 1, 1, 1, 1 }) || DES.IsWeakKey(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })) return 10;
        if (!DES.IsSemiWeakKey(new byte[] { 0x01, 0xFE, 0x01, 0xFE, 0x01, 0xFE, 0x01, 0xFE }) || !Throws<CryptographicException>(() => DES.IsWeakKey(new byte[7]))) return 11;
        byte[] repeated = new byte[16];
        for (int i = 0; i < 16; i++) repeated[i] = (byte)(i % 8 + 1);
        if (!TripleDES.IsWeakKey(repeated)) return 12;

        // Parameter values.
        if (RSAEncryptionPadding.OaepSHA256.Mode != RSAEncryptionPaddingMode.Oaep || RSAEncryptionPadding.OaepSHA256.OaepHashAlgorithm != HashAlgorithmName.SHA256) return 13;
        if (RSAEncryptionPadding.CreateOaep(HashAlgorithmName.SHA1) != RSAEncryptionPadding.OaepSHA1 || RSAEncryptionPadding.Pkcs1.Mode != RSAEncryptionPaddingMode.Pkcs1 || RSAEncryptionPadding.OaepSHA256.ToString() != "OaepSHA256") return 14;
        if (RSASignaturePadding.Pss.Mode != RSASignaturePaddingMode.Pss || RSASignaturePadding.Pkcs1 != RSASignaturePadding.Pkcs1 || RSASignaturePadding.Pss == RSASignaturePadding.Pkcs1) return 15;
        var pbe = new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000);
        if (pbe.EncryptionAlgorithm != PbeEncryptionAlgorithm.Aes256Cbc || pbe.IterationCount != 1000 || !Throws<ArgumentOutOfRangeException>(() => new PbeParameters(PbeEncryptionAlgorithm.Aes128Cbc, HashAlgorithmName.SHA1, -1))) return 16;
        var rsa = new RSAParameters { Modulus = new byte[] { 1 }, Exponent = new byte[] { 1, 0, 1 } };
        var dsa = new DSAParameters { Counter = 7, Y = new byte[] { 2 } };
        if (rsa.Exponent!.Length != 3 || dsa.Counter != 7 || dsa.Y![0] != 2) return 17;

        // Named curves, OIDs and EC parameter validation.
        ECCurve p256 = ECCurve.NamedCurves.nistP256;
        if (!p256.IsNamed || p256.Oid.Value != "1.2.840.10045.3.1.7" || p256.Oid.FriendlyName != "nistP256" || p256.CurveType != ECCurve.ECCurveType.Named) return 18;
        if (ECCurve.CreateFromValue("1.3.132.0.34").Oid.Value != "1.3.132.0.34" || !ECCurve.CreateFromFriendlyName("nistP384").IsNamed) return 19;
        p256.Validate();
        var ec = new ECParameters { Curve = p256, Q = new ECPoint { X = new byte[32], Y = new byte[32] } };
        ec.Validate();
        ec.Q.Y = new byte[31];
        if (!Throws<CryptographicException>(() => ec.Validate())) return 20;

        // Oid lookup uses upstream's static tables.
        var rsaOid = new Oid("1.2.840.113549.1.1.1");
        if (rsaOid.FriendlyName != "RSA" || new Oid("sha256").Value != "2.16.840.1.101.3.4.2.1") return 21;
        if (Oid.FromFriendlyName("sha256", OidGroup.HashAlgorithm).Value != "2.16.840.1.101.3.4.2.1" || Oid.FromOidValue("1.3.14.3.2.26", OidGroup.All).FriendlyName != "sha1") return 22;
        if (!Throws<CryptographicException>(() => Oid.FromFriendlyName("no-such-name", OidGroup.All)) || !Throws<ArgumentNullException>(() => Oid.FromOidValue(null!, OidGroup.All))) return 23;
        var oids = new OidCollection();
        if (oids.Add(rsaOid) != 0 || oids.Add(new Oid("1.3.14.3.2.26")) != 1 || oids.Count != 2 || oids["RSA"] != rsaOid || oids["1.3.14.3.2.26"]!.FriendlyName != "sha1") return 24;
        OidEnumerator e = oids.GetEnumerator();
        int seen = 0;
        while (e.MoveNext()) seen++;
        e.Reset();
        if (seen != 2 || !e.MoveNext() || e.Current != rsaOid) return 25;

        // CryptoConfig maps names to OIDs from upstream's default table and AddOID.
        if (CryptoConfig.MapNameToOID("SHA256") != "2.16.840.1.101.3.4.2.1" || CryptoConfig.MapNameToOID("System.Security.Cryptography.SHA1") != "1.3.14.3.2.26") return 26;
        if (CryptoConfig.MapNameToOID("no-such-algorithm") != null || !Throws<ArgumentNullException>(() => CryptoConfig.MapNameToOID(null!))) return 27;
        CryptoConfig.AddOID("1.2.3.4.5", "NetWasmFixtureName");
        if (CryptoConfig.MapNameToOID("netwasmfixturename") != "1.2.3.4.5") return 28;

        // Base64 transforms round trip through CryptoStream and reject malformed input.
        var encoded = new MemoryStream();
        using (var writer = new CryptoStream(encoded, new ToBase64Transform(), CryptoStreamMode.Write, leaveOpen: true))
            writer.Write(Encoding.ASCII.GetBytes("hello world"));
        if (Encoding.ASCII.GetString(encoded.ToArray()) != "aGVsbG8gd29ybGQ=") return 29;
        using var decoder = new CryptoStream(new MemoryStream(Encoding.ASCII.GetBytes(" aGVs bG8g\nd29y bGQ= ")), new FromBase64Transform(FromBase64TransformMode.IgnoreWhiteSpaces), CryptoStreamMode.Read);
        var decoded = new MemoryStream();
        decoder.CopyTo(decoded);
        if (Encoding.ASCII.GetString(decoded.ToArray()) != "hello world") return 30;
        var toB64 = new ToBase64Transform();
        if (toB64.InputBlockSize != 3 || toB64.OutputBlockSize != 4 || Encoding.ASCII.GetString(toB64.TransformFinalBlock(new byte[] { 1, 2 }, 0, 2)) != "AQI=") return 31;
        if (!Throws<FormatException>(() => new FromBase64Transform().TransformFinalBlock(Encoding.ASCII.GetBytes("a*==="), 0, 5))) return 32;

        // Constants keep their values.
        if ((int)CipherMode.CTS != 5 || (int)PaddingMode.ISO10126 != 5 || (int)OidGroup.KeyDerivationFunction != 10 || (int)PbeEncryptionAlgorithm.TripleDes3KeyPkcs12 != 4) return 33;
        if ((int)X509KeyUsageFlags.DigitalSignature != 128 || (int)X509ContentType.Pfx != 3 || (int)StoreName.My != 5 || (int)X509VerificationFlags.AllFlags != 4095) return 34;
        if ((int)X509ChainStatusFlags.NotTimeValid != 1 || (int)OpenFlags.ReadWrite != 1 || (int)X509FindType.FindBySubjectName != 1 || (int)StoreLocation.LocalMachine != 2) return 35;
        if ((int)X500DistinguishedNameFlags.Reversed != 1 || (int)X509RevocationMode.Online != 1 || (int)X509KeyStorageFlags.Exportable != 4 || (int)X509NameType.DnsName != 3) return 36;
        if ((int)X509IncludeOption.WholeChain != 3 || (int)X509RevocationFlag.ExcludeRoot != 2 || (int)X509SubjectKeyIdentifierHashAlgorithm.CapiSha1 != 2) return 37;

        // The default factory's key generation is managed; its ciphers are OpenSSL's, which the NetWasm.App
        // cryptography suite compares with desktop.
        using var platform = Aes.Create();
        if (platform.KeySize != 256 || platform.Key.Length != 32 || platform.IV.Length != 16) return 38;
        return input + 1;
    }
}
