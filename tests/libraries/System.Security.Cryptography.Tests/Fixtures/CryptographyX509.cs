// From the compiler harness's CryptographyX509CompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
#pragma warning disable SYSLIB0026, SYSLIB0027, SYSLIB0028, SYSLIB0051, SYSLIB0057, CS0618
using System;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
namespace CryptoX509Fixture;

// Explicit keys and signers: the X.509 types under test encode, decode and dispatch around them.
sealed class TestRsa : RSA
{
    RSAParameters _p;
    public TestRsa() { KeySizeValue = 1024; LegalKeySizesValue = new[] { new KeySizes(512, 4096, 64) }; }
    public override RSAParameters ExportParameters(bool includePrivateParameters)
    {
        if (includePrivateParameters) throw new CryptographicException("no private key");
        return new RSAParameters { Modulus = (byte[])_p.Modulus!.Clone(), Exponent = (byte[])_p.Exponent!.Clone() };
    }
    public override void ImportParameters(RSAParameters parameters)
    {
        _p = new RSAParameters { Modulus = (byte[])parameters.Modulus!.Clone(), Exponent = (byte[])parameters.Exponent!.Clone() };
        KeySizeValue = parameters.Modulus.Length * 8;
    }
    public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        byte[] r = new byte[KeySizeValue / 8];
        for (int i = 0; i < r.Length; i++) r[i] = (byte)(hash[i % hash.Length] ^ (padding == RSASignaturePadding.Pss ? 0x5A : 0xA5));
        return r;
    }
    public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding) =>
        SignHash(hash, hashAlgorithm, padding).AsSpan().SequenceEqual(signature);
}

sealed class TestEcdsa : ECDsa
{
    ECParameters _p;
    public TestEcdsa() { KeySizeValue = 256; LegalKeySizesValue = new[] { new KeySizes(256, 384, 128) }; }
    public override ECParameters ExportParameters(bool includePrivateParameters)
    {
        if (includePrivateParameters) throw new CryptographicException("no private key");
        return new ECParameters { Curve = _p.Curve, Q = new ECPoint { X = (byte[])_p.Q.X!.Clone(), Y = (byte[])_p.Q.Y!.Clone() } };
    }
    public override void ImportParameters(ECParameters parameters)
    {
        parameters.Validate();
        _p = new ECParameters { Curve = parameters.Curve, Q = new ECPoint { X = (byte[])parameters.Q.X!.Clone(), Y = (byte[])parameters.Q.Y!.Clone() } };
    }
    public override byte[] SignHash(byte[] hash)
    {
        byte[] r = new byte[64];
        for (int i = 0; i < r.Length; i++) r[i] = (byte)(hash[i % hash.Length] ^ (0x40 + i));
        return r;
    }
    public override bool VerifyHash(byte[] hash, byte[] signature) => SignHash(hash).AsSpan().SequenceEqual(signature);
}

// A signer that supplies its own algorithm identifier, signature and public key.
sealed class TestGenerator : X509SignatureGenerator
{
    readonly PublicKey _key;
    public int Signs, Builds;
    public TestGenerator(PublicKey key) { _key = key; }
    public override byte[] GetSignatureAlgorithmIdentifier(HashAlgorithmName hashAlgorithm) =>
        Convert.FromHexString("300D06092A864886F70D01010B0500");
    public override byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm) { Signs++; return SHA256.HashData(data); }
    protected override PublicKey BuildPublicKey() { Builds++; return _key; }
}

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }
    static string H(byte[] b) => Convert.ToHexString(b);
    static string D(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
#if GEN
    static bool Same(string name, string actual) { Console.WriteLine(name + "=" + actual); return true; }
#else
    static bool Same(string name, string actual) => Expected(name) == actual;
    static string Expected(string name) => name switch
    {
        "bc" => "30060101FF020103",
        "ku" => "030307A080",
        "eku" => "301406082B0601050507030106082B06010505070302",
        "ski" => "040401020304",
        "san" => "3062820B6578616D706C652E636F6D811075736572406578616D706C652E636F6D8704C0000201861868747470733A2F2F6578616D706C652E636F6D2F70617468A021060A2B060104018237140203A0130C117573657240636F72702E6578616D706C65",
        "rsaKey" => "387C783A97F04303A3E024D9D18A9E64360DA4623FFFEF0FB90634B7C73D7748",
        "skiSha1" => "4681A052E0F5611DFF94A74DE9D1E06DFDB903F1",
        "skiShort" => "49D1E06DFDB903F1",
        "skiCapi" => "DD4CC41DAE70A413F01324E0267CB4443EEDD456",
        "rsaAlg" => "300D06092A864886F70D01010B0500",
        "rsaSig" => "28330DF2AB90D659BF52480D2154805147CA3C2D41FDBD225FA30CAC1F85E660",
        "pssAlg" => "303D06092A864886F70D01010A3030A00D300B0609608648016503040202A11A301806092A864886F70D010108300B0609608648016503040202A203020130",
        "pssSig" => "BFD8A0B56CDADE6DA523E66EEEAD19F899C7DDADE2D2C46D1E9C15F5C2A81DEE",
        "ecAlg" => "300A06082A8648CE3D040302",
        "ecKey" => "3059301306072A8648CE3D020106082A8648CE3D030107034200040102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F20808182838485868788898A8B8C8D8E8F909192939495969798999A9B9C9D9E9F",
        "ecSig" => "3044022043D11A85B6858D0E641A71410159A1389C5E2AF8989B837F25DDFBF95D41A5DE022063F13AA596A5AD2E443A516121798118BC7E0AD8B8BBA35F05FDDBD97D6185FE",
        "csr" => "22F9F867137A4217DF674963BBE064A82AB9E64F74A9CAE6C3B60266FF5EDC7C",
        "csrPem" => "ABF9F74FE64A89470388FB524DB73162E48562DF4AD9535E4054D1456B88FF76",
        "rsaCsr" => "098F0855E88A723B9F53A2EB05A442C41E9F2D13ADD5F1E0DDB65C0231C2457D",
        "ecCsr" => "0C24CA0FA533A6421A193AC9186098DC5C11F0CFCEBEED90894E7EEA9F598687",
        _ => "?",
    };
#endif

    public static int Run(int input)
    {
        // Extensions encode with managed DER and decode lazily, rejecting malformed input.
        var bc = new X509BasicConstraintsExtension(true, true, 3, true);
        if (!bc.Critical || bc.Oid!.Value != "2.5.29.19" || !Same("bc", H(bc.RawData))) return 1;
        var bc2 = new X509BasicConstraintsExtension(new AsnEncodedData(bc.RawData), false);
        if (!bc2.CertificateAuthority || !bc2.HasPathLengthConstraint || bc2.PathLengthConstraint != 3 || bc2.Critical) return 2;
        var bcEmpty = new X509BasicConstraintsExtension();
        if (bcEmpty.Oid!.Value != "2.5.29.19" || bcEmpty.RawData.Length != 0 || bcEmpty.Critical) return 3;
        var ku = new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment | X509KeyUsageFlags.DecipherOnly, true);
        if (ku.Oid!.Value != "2.5.29.15" || !Same("ku", H(ku.RawData))) return 4;
        if (new X509KeyUsageExtension(new AsnEncodedData(ku.RawData), false).KeyUsages != ku.KeyUsages) return 5;
        var eku = new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2") }, false);
        if (!Same("eku", H(eku.RawData))) return 6;
        var eku2 = new X509EnhancedKeyUsageExtension(new AsnEncodedData(eku.RawData), true);
        if (eku2.EnhancedKeyUsages.Count != 2 || eku2.EnhancedKeyUsages[1].Value != "1.3.6.1.5.5.7.3.2" || !eku2.Critical) return 7;
        var ski = new X509SubjectKeyIdentifierExtension(new byte[] { 1, 2, 3, 4 }, false);
        if (ski.SubjectKeyIdentifier != "01020304" || !Same("ski", H(ski.RawData))) return 8;
        if (new X509SubjectKeyIdentifierExtension("0A0B0C", true).SubjectKeyIdentifier != "0A0B0C") return 9;
        if (!Throws<CryptographicException>(() => _ = new X509KeyUsageExtension(new AsnEncodedData(new byte[] { 1, 2 }), false).KeyUsages)) return 10;
        if (!Throws<CryptographicException>(() => _ = new X509BasicConstraintsExtension(new AsnEncodedData(new byte[] { 0x30, 0x05 }), false).CertificateAuthority)) return 11;
        if (!Throws<CryptographicException>(() => _ = new X509EnhancedKeyUsageExtension(new AsnEncodedData(new byte[] { 0x04, 0x00 }), false).EnhancedKeyUsages)) return 12;

        // Extension collections index by position and OID; plain extensions keep raw bytes.
        var extensions = new X509ExtensionCollection();
        if (extensions.Add(bc) != 0 || extensions.Add(ku) != 1 || extensions.Add(eku) != 2) return 13;
        if (extensions.Count != 3 || extensions["2.5.29.15"] != ku || extensions["2.5.29.99"] != null || extensions[0] != bc) return 14;
        if (extensions.IsSynchronized || extensions.SyncRoot != extensions) return 15;
        var extensionArray = new X509Extension[4];
        extensions.CopyTo(extensionArray, 1);
        if (extensionArray[0] != null || extensionArray[1] != bc || extensionArray[3] != eku) return 16;
        int count = 0;
        X509ExtensionEnumerator extensionEnumerator = extensions.GetEnumerator();
        while (extensionEnumerator.MoveNext()) count++;
        extensionEnumerator.Reset();
        if (count != 3 || !extensionEnumerator.MoveNext() || extensionEnumerator.Current != bc) return 17;
        if (!Throws<ArgumentOutOfRangeException>(() => _ = extensions[3]) || !Throws<InvalidOperationException>(() => _ = extensions[-1])) return 18;
        if (!Throws<ArgumentNullException>(() => extensions.Add(null!))) return 19;
        var raw = new X509Extension("1.2.3.4", new byte[] { 0x05, 0x00 }, true);
        if (raw.Oid!.Value != "1.2.3.4" || !raw.Critical || H(raw.RawData) != "0500") return 20;
        raw.Critical = false;
        var copied = new X509KeyUsageExtension();
        copied.CopyFrom(ku);
        if (copied.KeyUsages != ku.KeyUsages || !copied.Critical || raw.Critical) return 21;
        if (!Throws<ArgumentException>(() => raw.CopyFrom(new AsnEncodedData(new byte[] { 5, 0 })))) return 22;

        // Subject alternative names encode every general-name kind the builder offers.
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("example.com");
        san.AddEmailAddress("user@example.com");
        san.AddIpAddress(IPAddress.Parse("192.0.2.1"));
        san.AddUri(new Uri("https://example.com/path"));
        san.AddUserPrincipalName("user@corp.example");
        X509Extension sanExtension = san.Build(true);
        if (sanExtension.Oid!.Value != "2.5.29.17" || !sanExtension.Critical || !Same("san", H(sanExtension.RawData))) return 23;
        if (!Throws<ArgumentNullException>(() => san.AddIpAddress(null!))) return 24;

        // Distinguished names keep their DER; text decoding needs the platform name formatter.
        byte[] nameDer = Convert.FromHexString("300F310D300B06035504030C0454657374");
        var subject = new X500DistinguishedName(nameDer);
        nameDer[5] = 0;
        if (H(subject.RawData) != "300F310D300B06035504030C0454657374") return 25;
        var subjectCopy = new X500DistinguishedName(new AsnEncodedData(subject.RawData));
        int rdns = 0;
        foreach (X500RelativeDistinguishedName rdn in subjectCopy.EnumerateRelativeDistinguishedNames())
        {
            rdns++;
            if (rdn.HasMultipleElements || rdn.GetSingleElementType().Value != "2.5.4.3" || rdn.GetSingleElementValue() != "Test") return 26;
        }
        if (rdns != 1) return 27;
        // Names decode and encode in managed code (upstream's X500NameEncoder), as on Linux.
        if (subject.Name != "CN=Test" || subject.Decode(X500DistinguishedNameFlags.None) != "CN=Test") return 28;
        // Desktop .NET 11 on Linux encodes the name as a PrintableString.
        if (subject.Format(false) != "CN=Test" || H(new X500DistinguishedName("CN=Test").RawData) != "300F310D300B0603550403130454657374") return 29;

        // Public keys hold OID, parameters and key bits, and round-trip SubjectPublicKeyInfo.
        byte[] modulus = new byte[128];
        for (int i = 0; i < modulus.Length; i++) modulus[i] = (byte)(i * 7 + 3);
        modulus[0] = 0xC5;
        var rsa = new TestRsa();
        rsa.ImportParameters(new RSAParameters { Modulus = modulus, Exponent = new byte[] { 1, 0, 1 } });
        byte[] spki = rsa.ExportSubjectPublicKeyInfo();
        PublicKey publicKey = PublicKey.CreateFromSubjectPublicKeyInfo(spki, out int read);
        if (read != spki.Length || publicKey.Oid.Value != "1.2.840.113549.1.1.1" || H(publicKey.EncodedParameters!.RawData) != "0500") return 31;
        if (!Same("rsaKey", D(publicKey.EncodedKeyValue.RawData)) || H(publicKey.ExportSubjectPublicKeyInfo()) != H(spki)) return 32;
        var rebuilt = new PublicKey(new Oid("1.2.840.113549.1.1.1"), new AsnEncodedData(publicKey.EncodedParameters.RawData), new AsnEncodedData(publicKey.EncodedKeyValue.RawData));
        if (H(rebuilt.ExportSubjectPublicKeyInfo()) != H(spki) || H(new PublicKey(rsa).ExportSubjectPublicKeyInfo()) != H(spki)) return 33;
        if (publicKey.GetECDsaPublicKey() != null || publicKey.GetECDiffieHellmanPublicKey() != null) return 34;
        // Its Key and GetRSAPublicKey are OpenSSL keys, which the NetWasm.App cryptography suite covers.
        if (!Same("skiSha1", new X509SubjectKeyIdentifierExtension(publicKey, false).SubjectKeyIdentifier!)) return 36;
        if (!Same("skiShort", new X509SubjectKeyIdentifierExtension(publicKey, X509SubjectKeyIdentifierHashAlgorithm.ShortSha1, false).SubjectKeyIdentifier!)) return 37;
        if (!Same("skiCapi", new X509SubjectKeyIdentifierExtension(publicKey, X509SubjectKeyIdentifierHashAlgorithm.CapiSha1, false).SubjectKeyIdentifier!)) return 38;

        // The built-in signature generators dispatch to the supplied keys.
        X509SignatureGenerator rsaGenerator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pkcs1);
        if (!Same("rsaAlg", H(rsaGenerator.GetSignatureAlgorithmIdentifier(HashAlgorithmName.SHA256))) || H(rsaGenerator.PublicKey.ExportSubjectPublicKeyInfo()) != H(spki)) return 39;
        if (!Same("rsaSig", D(rsaGenerator.SignData(new byte[] { 1, 2, 3 }, HashAlgorithmName.SHA256)))) return 40;
        X509SignatureGenerator pssGenerator = X509SignatureGenerator.CreateForRSA(rsa, RSASignaturePadding.Pss);
        if (!Same("pssAlg", H(pssGenerator.GetSignatureAlgorithmIdentifier(HashAlgorithmName.SHA384))) || !Same("pssSig", D(pssGenerator.SignData(new byte[] { 4 }, HashAlgorithmName.SHA384)))) return 41;
        byte[] x = new byte[32], y = new byte[32];
        for (int i = 0; i < 32; i++) { x[i] = (byte)(i + 1); y[i] = (byte)(0x80 + i); }
        var ecdsa = new TestEcdsa();
        ecdsa.ImportParameters(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
        X509SignatureGenerator ecGenerator = X509SignatureGenerator.CreateForECDsa(ecdsa);
        if (!Same("ecAlg", H(ecGenerator.GetSignatureAlgorithmIdentifier(HashAlgorithmName.SHA256))) || !Same("ecKey", H(ecGenerator.PublicKey.ExportSubjectPublicKeyInfo()))) return 43;
        if (!Same("ecSig", H(ecGenerator.SignData(new byte[] { 1, 2, 3 }, HashAlgorithmName.SHA256)))) return 44;
        if (!Throws<ArgumentOutOfRangeException>(() => ecGenerator.GetSignatureAlgorithmIdentifier(HashAlgorithmName.MD5))) return 45;
        if (!Throws<ArgumentNullException>(() => X509SignatureGenerator.CreateForECDsa(null!)) || !Throws<ArgumentNullException>(() => X509SignatureGenerator.CreateForRSA(rsa, null!))) return 46;

        // Certification requests encode as managed DER and sign through the explicit signer.
        var request = new CertificateRequest(subject, publicKey, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(bc);
        request.CertificateExtensions.Add(ku);
        request.CertificateExtensions.Add(sanExtension);
        if (request.SubjectName != subject || request.PublicKey != publicKey || request.HashAlgorithm != HashAlgorithmName.SHA256) return 47;
        if (!Throws<InvalidOperationException>(() => request.CreateSigningRequest())) return 48;
        var generator = new TestGenerator(publicKey);
        byte[] csr = request.CreateSigningRequest(generator);
        if (generator.Signs != 1 || !Same("csr", D(csr))) return 49;
        if (!Same("csrPem", D(System.Text.Encoding.ASCII.GetBytes(request.CreateSigningRequestPem(generator))))) return 50;
        if (!Same("rsaCsr", D(new CertificateRequest(subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1).CreateSigningRequest()))) return 51;
        if (!Same("ecCsr", D(new CertificateRequest(subject, ecdsa, HashAlgorithmName.SHA384).CreateSigningRequest()))) return 52;

        // Issuing a certificate signs, then needs a certificate backend to materialize it.
        var notBefore = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var notAfter = notBefore.AddYears(1);
        int signs = generator.Signs;
        // The certificate itself is OpenSSL's, which the NetWasm.App cryptography suite covers; the
        // validation before signing is managed.
        if (!Throws<ArgumentException>(() => request.Create(subject, generator, notAfter, notBefore, new byte[] { 1 }))) return 56;
        if (!Throws<InvalidOperationException>(() => request.CreateSelfSigned(notBefore, notAfter))) return 57;
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        if (!Throws<InvalidOperationException>(() => request.Create(subject, generator, notBefore, notAfter, new byte[] { 1 }))) return 58;

        // Default certificates are empty: identity members work, data members reject the empty handle.
        var empty = new X509Certificate2();
        if (empty.Handle != IntPtr.Zero || empty.GetHashCode() != 0) return 59;
        if (empty.ToString() != typeof(X509Certificate2).ToString() || empty.ToString(true) != typeof(X509Certificate2).ToString()) return 60;
        if (!Throws<CryptographicException>(() => _ = empty.Thumbprint) || !Throws<CryptographicException>(() => _ = empty.Subject) || !Throws<CryptographicException>(() => _ = empty.PublicKey)) return 61;
        if (!Throws<CryptographicException>(() => _ = empty.HasPrivateKey) || !Throws<CryptographicException>(() => _ = empty.Extensions)) return 62;
        if (!Throws<CryptographicException>(() => empty.GetRSAPublicKey()) || !Throws<CryptographicException>(() => empty.CopyWithPrivateKey(rsa))) return 63;
        var other = new X509Certificate2();
        if (!empty.Equals(other) || empty.Equals((X509Certificate?)null) || !empty.Equals((object)other)) return 64;
        var legacy = new X509Certificate();
        if (legacy.ToString() != typeof(X509Certificate).ToString() || legacy.ToString(true) != typeof(X509Certificate).ToString()) return 65;
        empty.Reset();
        empty.Dispose();
        legacy.Dispose();
        if (!Throws<ArgumentException>(() => new X509Certificate2(new byte[0], "pw"))) return 66;
        if (!Throws<ArgumentException>(() => new X509Certificate2(new byte[] { 1 }, "pw", (X509KeyStorageFlags)0x1000))) return 67;
        if (!Throws<ArgumentException>(() => new X509Certificate2(new byte[] { 1 }, (string?)null, X509KeyStorageFlags.EphemeralKeySet | X509KeyStorageFlags.PersistKeySet))) return 68;
        if (!Throws<ArgumentNullException>(() => new X509Certificate2((string)null!))) return 69;
        if (!Throws<PlatformNotSupportedException>(() => new X509Certificate(null!, default(System.Runtime.Serialization.StreamingContext)))) return 70;

        // Collections compare certificates by value; the legacy collection keeps CollectionBase rules.
        var a = new X509Certificate2();
        var b = new X509Certificate2();
        var collection = new X509Certificate2Collection();
        if (collection.Add(a) != 0 || collection.Count != 1 || collection[0] != a) return 76;
        collection.AddRange(new[] { b, a });
        collection.Insert(1, b);
        if (collection.Count != 4 || !collection.Contains(b) || collection[1] != b) return 77;
        collection.Remove(b);
        if (collection.Count != 3 || collection[0] != b) return 78;
        var collectionCopy = new X509Certificate2Collection(collection);
        collection.RemoveRange(new[] { a, b });
        if (collection.Count != 1 || collectionCopy.Count != 3 || new X509Certificate2Collection(a).Count != 1) return 79;
        int seen = 0;
        X509Certificate2Enumerator certificateEnumerator = collectionCopy.GetEnumerator();
        while (certificateEnumerator.MoveNext()) if (certificateEnumerator.Current != null) seen++;
        certificateEnumerator.Reset();
        if (seen != 3 || !certificateEnumerator.MoveNext()) return 80;
        collectionCopy[2] = a;
        if (collectionCopy[2] != a || !Throws<ArgumentNullException>(() => collection.Add(null!)) || !Throws<ArgumentNullException>(() => collection.AddRange((X509Certificate2[])null!))) return 81;
        var legacyCollection = new X509CertificateCollection(new X509Certificate[] { a, b });
        if (legacyCollection.IndexOf(b) != 0 || legacyCollection.GetHashCode() != 0 || legacyCollection.Count != 2) return 82;
        var legacyArray = new X509Certificate[3];
        legacyCollection.CopyTo(legacyArray, 1);
        if (legacyArray[1] != a || legacyArray[2] != b) return 83;
        X509CertificateCollection.X509CertificateEnumerator legacyEnumerator = legacyCollection.GetEnumerator();
        if (!legacyEnumerator.MoveNext() || legacyEnumerator.Current != a) return 84;
        legacyCollection.AddRange(new X509CertificateCollection(new X509Certificate[] { a }));
        legacyCollection.Insert(0, b);
        legacyCollection.Remove(a);
        if (legacyCollection.Count != 3 || !legacyCollection.Contains(a) || !Throws<ArgumentNullException>(() => legacyCollection.Add(null!))) return 85;

        // Chains are configuration plus empty results until a backend builds them.
        using (var chain = new X509Chain())
        {
            X509ChainPolicy policy = chain.ChainPolicy;
            if (policy.RevocationMode != X509RevocationMode.Online || policy.RevocationFlag != X509RevocationFlag.ExcludeRoot) return 89;
            if (policy.VerificationFlags != X509VerificationFlags.NoFlag || policy.UrlRetrievalTimeout != TimeSpan.Zero || policy.ApplicationPolicy.Count != 0 || policy.ExtraStore.Count != 0) return 90;
            policy.RevocationMode = X509RevocationMode.NoCheck;
            policy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            policy.ApplicationPolicy.Add(new Oid("1.3.6.1.5.5.7.3.1"));
            policy.CertificatePolicy.Add(new Oid("2.5.29.32.0"));
            policy.ExtraStore.Add(a);
            policy.UrlRetrievalTimeout = TimeSpan.FromSeconds(5);
            policy.VerificationTime = new DateTime(2024, 6, 1);
            if (policy.RevocationMode != X509RevocationMode.NoCheck || policy.ExtraStore.Count != 1 || policy.UrlRetrievalTimeout != TimeSpan.FromSeconds(5) || policy.VerificationTime != new DateTime(2024, 6, 1)) return 91;
            if (!Throws<ArgumentException>(() => policy.RevocationMode = (X509RevocationMode)9) || !Throws<ArgumentException>(() => policy.RevocationFlag = (X509RevocationFlag)9)) return 92;
            policy.UrlRetrievalTimeout = TimeSpan.FromSeconds(-1);
            if (policy.UrlRetrievalTimeout != TimeSpan.FromSeconds(-1)) return 92;
            policy.Reset();
            if (policy.RevocationMode != X509RevocationMode.Online || policy.ApplicationPolicy.Count != 0 || policy.ExtraStore.Count != 0 || policy.VerificationFlags != X509VerificationFlags.NoFlag) return 93;
            var replacement = new X509ChainPolicy { RevocationFlag = X509RevocationFlag.EntireChain };
            chain.ChainPolicy = replacement;
            if (chain.ChainPolicy != replacement || !Throws<ArgumentNullException>(() => chain.ChainPolicy = null!)) return 94;
            X509ChainElementCollection elements = chain.ChainElements;
            if (elements.Count != 0 || elements.IsSynchronized || elements.SyncRoot != elements || chain.ChainStatus.Length != 0) return 95;
            elements.CopyTo(new X509ChainElement[1], 0);
            if (elements.GetEnumerator().MoveNext() || chain.ChainContext != IntPtr.Zero) return 96;
            if (!Throws<ArgumentException>(() => chain.Build(new X509Certificate2()))) return 97;
            chain.Reset();
        }
        using (X509Chain created = X509Chain.Create())
        {
            if (created.ChainPolicy.RevocationMode != X509RevocationMode.Online) return 98;
        }
        var status = new X509ChainStatus { Status = X509ChainStatusFlags.NotTimeValid, StatusInformation = "expired" };
        if (status.Status != X509ChainStatusFlags.NotTimeValid || status.StatusInformation != "expired") return 99;

        // Stores keep their name and location while closed; opening needs a platform store.
        using (var store = new X509Store(StoreName.My, StoreLocation.CurrentUser))
        {
            if (store.Name != "My" || store.Location != StoreLocation.CurrentUser || store.IsOpen || store.Certificates.Count != 0) return 100;
            if (!Throws<CryptographicException>(() => store.Add(a)) || !Throws<CryptographicException>(() => _ = store.StoreHandle)) return 101;
            store.Close();
        }
        if (new X509Store().Name != "MY" || new X509Store("Root").Name != "Root" || new X509Store(StoreLocation.LocalMachine).Location != StoreLocation.LocalMachine) return 102;
        if (new X509Store(StoreName.TrustedPeople).Name != "TrustedPeople" || !Throws<ArgumentException>(() => new X509Store(StoreName.My, (StoreLocation)7))) return 103;
        return input + 1;
    }
}
