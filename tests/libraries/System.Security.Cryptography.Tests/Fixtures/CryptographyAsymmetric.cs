// From the compiler harness's CryptographyAsymmetricCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
#pragma warning disable SYSLIB0021, SYSLIB0042, SYSLIB0043, SYSLIB0045, SYSLIB0048, CS0618
using System;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
namespace CryptoAsymmetricFixture;

// Explicit subclasses supply the keys and the private-key operations; the bases under test
// provide parameter formats, hashing, adapters and validation around them.
sealed class TestRsa : RSA
{
    RSAParameters _p;
    public RSAEncryptionPadding? LastEncryptionPadding;
    public RSASignaturePadding? LastSignaturePadding;
    public HashAlgorithmName LastHashName;
    public byte[]? LastHash;
    public TestRsa() { KeySizeValue = 1024; LegalKeySizesValue = new[] { new KeySizes(512, 4096, 64) }; }
    public override RSAParameters ExportParameters(bool includePrivateParameters)
    {
        if (_p.Modulus is null) throw new CryptographicException("no key");
        if (includePrivateParameters && _p.D is null) throw new CryptographicException("no private key");
        // Like real keys, hand out copies: the export helpers zero the private arrays after use.
        RSAParameters r = new RSAParameters { Modulus = C(_p.Modulus), Exponent = C(_p.Exponent) };
        if (includePrivateParameters) { r.D = C(_p.D); r.P = C(_p.P); r.Q = C(_p.Q); r.DP = C(_p.DP); r.DQ = C(_p.DQ); r.InverseQ = C(_p.InverseQ); }
        return r;
    }
    public override void ImportParameters(RSAParameters parameters)
    {
        // Keep copies: the import helpers zero the arrays they pass in.
        _p = new RSAParameters { Modulus = C(parameters.Modulus), Exponent = C(parameters.Exponent), D = C(parameters.D), P = C(parameters.P), Q = C(parameters.Q), DP = C(parameters.DP), DQ = C(parameters.DQ), InverseQ = C(parameters.InverseQ) };
        KeySizeValue = parameters.Modulus!.Length * 8;
    }
    static byte[]? C(byte[]? b) => (byte[]?)b?.Clone();
    static byte[] Reverse(byte[] data) { byte[] r = (byte[])data.Clone(); Array.Reverse(r); return r; }
    public override byte[] Encrypt(byte[] data, RSAEncryptionPadding padding) { LastEncryptionPadding = padding; return Reverse(data); }
    public override byte[] Decrypt(byte[] data, RSAEncryptionPadding padding) { LastEncryptionPadding = padding; return Reverse(data); }
    public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        LastHash = hash; LastHashName = hashAlgorithm; LastSignaturePadding = padding;
        byte[] r = (byte[])hash.Clone();
        for (int i = 0; i < r.Length; i++) r[i] ^= 0xA5;
        return r;
    }
    public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding) =>
        SignHash(hash, hashAlgorithm, padding).AsSpan().SequenceEqual(signature);
}

sealed class TestDsa : DSA
{
    DSAParameters _p;
    public byte[]? LastHash;
    public TestDsa() { KeySizeValue = 1024; LegalKeySizesValue = new[] { new KeySizes(512, 3072, 64) }; }
    public override DSAParameters ExportParameters(bool includePrivateParameters)
    {
        DSAParameters r = new DSAParameters { P = C(_p.P), Q = C(_p.Q), G = C(_p.G), Y = C(_p.Y) };
        if (includePrivateParameters) r.X = C(_p.X) ?? throw new CryptographicException("no private key");
        return r;
    }
    public override void ImportParameters(DSAParameters parameters)
    {
        _p = new DSAParameters { P = C(parameters.P), Q = C(parameters.Q), G = C(parameters.G), Y = C(parameters.Y), X = C(parameters.X) };
        KeySizeValue = parameters.P!.Length * 8;
    }
    static byte[]? C(byte[]? b) => (byte[]?)b?.Clone();
    public override byte[] CreateSignature(byte[] rgbHash)
    {
        LastHash = rgbHash;
        byte[] r = new byte[40];
        for (int i = 0; i < r.Length; i++) r[i] = (byte)(rgbHash[i % rgbHash.Length] ^ 0x3C);
        return r;
    }
    public override bool VerifySignature(byte[] rgbHash, byte[] rgbSignature) => CreateSignature(rgbHash).AsSpan().SequenceEqual(rgbSignature);
}

sealed class TestEcdsa : ECDsa
{
    ECParameters _p;
    public byte[]? LastHash;
    public TestEcdsa() { KeySizeValue = 256; LegalKeySizesValue = new[] { new KeySizes(256, 384, 128) }; }
    public override ECParameters ExportParameters(bool includePrivateParameters)
    {
        ECParameters r = new ECParameters { Curve = _p.Curve, Q = new ECPoint { X = C(_p.Q.X), Y = C(_p.Q.Y) } };
        if (includePrivateParameters) r.D = C(_p.D) ?? throw new CryptographicException("no private key");
        return r;
    }
    public override void ImportParameters(ECParameters parameters)
    {
        parameters.Validate();
        _p = new ECParameters { Curve = parameters.Curve, Q = new ECPoint { X = C(parameters.Q.X), Y = C(parameters.Q.Y) }, D = C(parameters.D) };
    }
    static byte[]? C(byte[]? b) => (byte[]?)b?.Clone();
    public override byte[] SignHash(byte[] hash)
    {
        LastHash = hash;
        byte[] r = new byte[64];
        for (int i = 0; i < r.Length; i++) r[i] = (byte)(hash[i % hash.Length] ^ (0x40 + i));
        return r;
    }
    public override bool VerifyHash(byte[] hash, byte[] signature) => SignHash(hash).AsSpan().SequenceEqual(signature);
}

sealed class TestEcdhPublicKey : ECDiffieHellmanPublicKey
{
    readonly ECParameters _p;
    public TestEcdhPublicKey(ECParameters p) { _p = p; }
    public override ECParameters ExportParameters() => new ECParameters { Curve = _p.Curve, Q = _p.Q };
}

sealed class BlobPublicKey : ECDiffieHellmanPublicKey
{
    public BlobPublicKey(byte[] blob) : base(blob) { }
}

sealed class TestEcdh : ECDiffieHellman
{
    ECParameters _p;
    public TestEcdh() { KeySizeValue = 256; }
    public override ECDiffieHellmanPublicKey PublicKey => new TestEcdhPublicKey(_p);
    public override ECParameters ExportParameters(bool includePrivateParameters)
    {
        ECParameters r = new ECParameters { Curve = _p.Curve, Q = new ECPoint { X = (byte[]?)_p.Q.X?.Clone(), Y = (byte[]?)_p.Q.Y?.Clone() } };
        if (includePrivateParameters) r.D = (byte[]?)_p.D?.Clone();
        return r;
    }
    public override void ImportParameters(ECParameters parameters) =>
        _p = new ECParameters { Curve = parameters.Curve, Q = new ECPoint { X = (byte[]?)parameters.Q.X?.Clone(), Y = (byte[]?)parameters.Q.Y?.Clone() }, D = (byte[]?)parameters.D?.Clone() };
    public override byte[] DeriveKeyFromHash(ECDiffieHellmanPublicKey otherPartyPublicKey, HashAlgorithmName hashAlgorithm, byte[]? secretPrepend, byte[]? secretAppend)
    {
        byte[] x = otherPartyPublicKey.ExportParameters().Q.X!;
        byte[] input = new byte[(secretPrepend?.Length ?? 0) + x.Length + (secretAppend?.Length ?? 0)];
        secretPrepend?.CopyTo(input, 0);
        x.CopyTo(input, secretPrepend?.Length ?? 0);
        secretAppend?.CopyTo(input, input.Length - (secretAppend?.Length ?? 0));
        return CryptographicOperations.HashData(hashAlgorithm, input);
    }
}

sealed class BareAlgorithm : AsymmetricAlgorithm
{
    public bool Disposed;
    public BareAlgorithm() { LegalKeySizesValue = new[] { new KeySizes(256, 1024, 256) }; KeySizeValue = 512; }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}

sealed class HashOnlyDeformatter : AsymmetricSignatureDeformatter
{
    public byte[]? Seen;
    public override void SetKey(AsymmetricAlgorithm key) { }
    public override void SetHashAlgorithm(string strName) { }
    public override bool VerifySignature(byte[] rgbHash, byte[] rgbSignature) { Seen = rgbHash; return rgbSignature.Length == 1; }
}

sealed class CspAlgorithm : ICspAsymmetricAlgorithm
{
    byte[] _blob = new byte[] { 7 };
    public CspKeyContainerInfo CspKeyContainerInfo => throw new NotSupportedException();
    public byte[] ExportCspBlob(bool includePrivateParameters) => (byte[])_blob.Clone();
    public void ImportCspBlob(byte[] rawData) => _blob = (byte[])rawData.Clone();
}

sealed class RsaSignatureDescription : SignatureDescription
{
    public override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key)
    {
        RSAPKCS1SignatureFormatter formatter = new RSAPKCS1SignatureFormatter(key);
        formatter.SetHashAlgorithm(DigestAlgorithm!);
        return formatter;
    }
}

public static class EntryPoint
{
    const string RsaN = "B6EB9B6218D8BDBD863FD8DFF0A78AECDD387EB1FDB4C242A501BBE18ED5487D24CD4B0ACF6B1329FCC79B2A3EE82E3DD3259B12E53B52C388A903EAAB25D3A51A19F3CB6B0B301A3A977F64A11AB0208612311373B924E043E3AE3CAF83843CB004D3D8AA134F8F3D793E2CF071B0BCE1DBB73D27D11D6A91A70C7CAA70BFB7";
    const string RsaE = "010001";
    const string RsaD = "435468066CB4E2EA6F0EF34A42AFB8F044E175A91884BF787E7A2BE8F3854840601A7CC3C6E357D37DAB0F4F368689F6D1D0A6CB06BB965448EA4579CC7388FA3E3E105E146D0E4C004CDB42CE73CD282B11EA0FD878220CA460543B80D37B91C2838DBC0AB9FF4592061C73A2B42847566C349177E3140F0E2273480A40A821";
    const string RsaP = "E08C3BC94AFF6852EE18700B4941223A3EADAD8023401658A7FB3FF27558647337E6ED6351F745E430D30BCD4CA3338BF2406BC1AD7EDCB8E1BF6A88D384F311";
    const string RsaQ = "D08AB5834CCEA2A7F9C3B72767A26AC3395F78C39AEDC94C2FB4980FC8E75C4E65D162105A53BB2452CA04CEA1CD9650D6F2D334CB92DF176F83C6FBF10FF647";
    const string RsaDP = "4756A623E46CD2630E7BBB150AD7E39CE78EDC014AB1B0A7C7EE7DB974E8793955B84B10F3D8805EE7D814F6CB734A0C1B11B66A58C9662C62A4229F87BBC451";
    const string RsaDQ = "81951F72E600245C423BD60C3B3E561ED700B716BCA77304CF43E595315E6A2220EC7985F5CABFA7B375EA84C74FEB4E72325301245A301BECE4327A0CF1C133";
    const string RsaQI = "3330A7E4D235A822F827A96C5868751989E442573E24A8D71F97914BCF1043B43312DB503515BC0B45EE06091AF021F886BBFF93F6093A5D2CEE3518D71AAFEF";
    const string EcX = "32CCCBD9070C0F9F0ADC64594E86A5650718ED11C52EF2738304FB7958A9CC48";
    const string EcY = "6E3DAF0DF23ECB52AA5171FDE0E3870637F09D609B764ABD8D4262FE17BCDCC7";
    const string EcD = "05229623B90EF626A13FC62913471535B709D38265BA5FD5A7ECD5A786D60839";
    const string DsaP = "EB221EF8BF60BCC1FED8DD251941432310D0F59869CAA3913C0E20643DC1B66648D842048A7B1607DD41595DC7C9FB978C9EFEF23034C1DCD822C47CCCBCD9BCABC641678882D78EDB6D0218543925E1DC80AF3C27B201561ECE8D72AEF45487D21E7922C346698FB85DC08AB033BE848FE29D0652E839DDA06B33B74D11F971";
    const string DsaQ = "87156A5B01869C530A7DF3889AD1199B1680C659";
    const string DsaG = "676A099D9DD4C35B804B087FED820BB72DC2E250B8FD3BC36E6F02E3DE2560DE67F25364D47E80CFC08C2E15E48502C93267567F282861CA3E5FE3CB881D4CDE9523B95580516B4BD8B6BA961B9622F3C768086797B1EB1E7C5B8F2E492678609069150A82E762E8B147E62B2CC02A38A7D246515137696EEACEA971F2C7728C";
    const string DsaY = "CCE9C2A089FBEDDBBFCE736452E995A2E6ABA4F1B0982E0D3048DD74C358A9A8C556F4B604C3297A0FC5D515A79D6E8D16D154D50524727C4C97E49B518FC2CA111BBC672D78CA80BE549D7B84FF8B1535DC67A20D7D270B079625539CB2322DD5C76263260584FA99400545E0B516C8EBA24A3D9E201F22463A8BBF68519B62";
    const string DsaX = "59441838853AAD10BFA9FDF7F2E881D94114D920";
    const string RsaEncPkcs8 = "308202E4305E06092A864886F70D01050D3051303006092A864886F70D01050C30230410FDF7572C20DB6B09EB59694774C2D53E020102300C06082A864886F70D02090500301D0609608648016503040102041085A0A1A9E6107A6F47D920F0F85E1D3A0482028042F46A98CA04D144CBA6E8B5A7A97F7DE43AFCC7D9545114ED7864D496C07491F2C1A7D880C1C2A62AC57F017555D3AF3035FD6BD7299793FD10147763F4EC69502FBE28343BF739BA2747A9AD97F2C71BF264D0CA7D8B84FE989134F07DA66A461E4F998B0D810A22FC7CDA12EB0336ED8DCB629EED70F41C3ABCDBAB1F1CC8070F012BCA44C9EE7215DD7A3F3F91D1F1651100D278A609B3837E2796AE47AA777444A326702E15D1320F7FAD98A2CA4CF652054AE69E3EC1D2220374C2816201E0C651B73669A1864F48F7CDBE908B57412945D9F74D287CA27C69AFDD41DC697067EAF15A2044CEC8CFBC2B438EA278C790C158A6D625E409CC63D3F553EB5CABC276FDF0A48BE2D2CFD57F50C2E1828641FB25019D3DD626221B4C1638A9EFD650A1B7524D304C9A14F7BD627E52815033DBD0F341C69E0CCD0992C338C5A6C43278EF808FA029803FE5F02459A5B6D3BCBE15885B4CC8BA48F5DF3854E244A44DD870938BE0A7EB97D649638C14E6D2A0EE4E16127CF4F000D41C6C55B072FBD6D794649FD3A22D111FD409CD02B19A2DB0DF29465A4A6FAC41E70A929AC53012A1009D475154CA26A671A887708D7EF86D5002CF8CF7EA4A217F10D38FE4594C0615CBDB3F3E36C99757376F800E5D2AEFB3EF502FC422B45988C2824E6EE25028CCD6A56E7BDD0BC95225C949F9B5CE98A0D5AD11FF26D7EB5FD345074DD552BD600A041C34C33194109CC87FE3745F2FA4A3B6DE1C4666D1828E2EE3A26A9F85B51D46F9E02AB26F9359AE7C26B2F5ED3F0B50B30575EA1CEB93B3770BB04B0D87DBFABEE1A40D3E7B61C3C591F0FF4B89BA6A3E97751CDBA7B93DC40381C74BB68CDAE316E282BD2D8D324C8BEC453E939C7C1C361792AB6E7FFAF2";

    static byte[] X(string hex) => Convert.FromHexString(hex);
    static string S(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    static bool Same(byte[]? a, byte[]? b) => a is not null && b is not null && a.AsSpan().SequenceEqual(b);

    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    static RSAParameters RsaKey() => new RSAParameters
    {
        Modulus = X(RsaN), Exponent = X(RsaE), D = X(RsaD), P = X(RsaP), Q = X(RsaQ),
        DP = X(RsaDP), DQ = X(RsaDQ), InverseQ = X(RsaQI),
    };

    static ECParameters EcKey() => new ECParameters
    {
        Curve = ECCurve.NamedCurves.nistP256,
        Q = new ECPoint { X = X(EcX), Y = X(EcY) },
        D = X(EcD),
    };

    static DSAParameters DsaKey() => new DSAParameters { P = X(DsaP), Q = X(DsaQ), G = X(DsaG), Y = X(DsaY), X = X(DsaX) };

    public static int Run(int input)
    {
        int rsa = Rsa();
        if (rsa != 0) return rsa;
        int formatters = RsaAdapters();
        if (formatters != 0) return formatters;
        int dsa = Dsa();
        if (dsa != 0) return dsa;
        int ec = Ec();
        if (ec != 0) return ec;
        int asn = AsnData();
        if (asn != 0) return asn;
        int bases = Bases();
        if (bases != 0) return bases;
        return input + 1;
    }

    static int Rsa()
    {
        using TestRsa rsa = new TestRsa();
        if (rsa.SignatureAlgorithm != "RSA" || rsa.KeyExchangeAlgorithm != "RSA" || rsa.KeySize != 1024) return 100;
        if (!Throws<CryptographicException>(() => rsa.KeySize = 100)) return 101;
        rsa.ImportParameters(RsaKey());

        // Key formats are DER written by the managed ASN.1 writer; the digests are desktop .NET's exports.
        byte[] pub = rsa.ExportRSAPublicKey();
        byte[] priv = rsa.ExportRSAPrivateKey();
        byte[] spki = rsa.ExportSubjectPublicKeyInfo();
        byte[] pkcs8 = rsa.ExportPkcs8PrivateKey();
        if (pub.Length != 140 || S(pub) != "996EF7C3F018A68A6DFDBF6E9D2B1B4A645361E7DA6B8944811B18D504216EC0") return 102;
        if (priv.Length != 608 || S(priv) != "798E9BA226D1546C3DD692DFD89E98198D5003ED668DB41948A728BE6852E3F7") return 103;
        if (spki.Length != 162 || S(spki) != "5542DFB80BE0BFF6860DE98C232BDAF1F98696A57447EA578289D8F78031FF43") return 104;
        if (pkcs8.Length != 634 || S(pkcs8) != "762F27A1A63824080B0C4D426585DCCF73D2E4FEB1D6A9737A9BED2BB5EC4CFC") return 105;
        if (rsa.TryExportRSAPublicKey(new byte[pub.Length - 1], out int written) || written != 0) return 106;
        byte[] exact = new byte[spki.Length];
        if (!rsa.TryExportSubjectPublicKeyInfo(exact, out written) || written != spki.Length || !Same(exact, spki)) return 107;

        using TestRsa copy = new TestRsa();
        byte[] trailing = new byte[pub.Length + 3];
        pub.CopyTo(trailing, 0);
        copy.ImportRSAPublicKey(trailing, out int read);
        if (read != pub.Length || !Same(copy.ExportParameters(false).Modulus, X(RsaN)) || copy.KeySize != 1024) return 108;
        copy.ImportSubjectPublicKeyInfo(spki, out read);
        if (read != spki.Length || !Same(copy.ExportSubjectPublicKeyInfo(), spki)) return 109;
        copy.ImportPkcs8PrivateKey(pkcs8, out read);
        if (read != pkcs8.Length || !Same(copy.ExportParameters(true).D, X(RsaD))) return 110;
        copy.ImportRSAPrivateKey(priv, out read);
        if (read != priv.Length || !Same(copy.ExportRSAPrivateKey(), priv)) return 111;
        if (!Throws<CryptographicException>(() => copy.ImportRSAPublicKey(new byte[] { 0x30, 0x03, 0x02, 0x01 }, out _))) return 112;
        using (TestEcdsa ecKey = new TestEcdsa())
        {
            ecKey.ImportParameters(EcKey());
            byte[] ecSpki = ecKey.ExportSubjectPublicKeyInfo();
            if (!Throws<CryptographicException>(() => copy.ImportSubjectPublicKeyInfo(ecSpki, out _))) return 113;
        }

        // PEM and XML text forms.
        string pem = rsa.ExportRSAPublicKeyPem();
        if (pem != "-----BEGIN RSA PUBLIC KEY-----\nMIGJAoGBALbrm2IY2L29hj/Y3/CniuzdOH6x/bTCQqUBu+GO1Uh9JM1LCs9rEyn8\nx5sqPuguPdMlmxLlO1LDiKkD6qsl06UaGfPLawswGjqXf2ShGrAghhIxE3O5JOBD\n4648r4OEPLAE09iqE0+PPXk+LPBxsLzh27c9J9EdapGnDHyqcL+3AgMBAAE=\n-----END RSA PUBLIC KEY-----") return 114;
        copy.ImportFromPem(rsa.ExportPkcs8PrivateKeyPem());
        if (!Same(copy.ExportPkcs8PrivateKey(), pkcs8)) return 115;
        if (!rsa.ExportSubjectPublicKeyInfoPem().StartsWith("-----BEGIN PUBLIC KEY-----\n", StringComparison.Ordinal)) return 116;
        if (!Throws<ArgumentException>(() => copy.ImportFromPem("no pem here"))) return 117;
        string xml = rsa.ToXmlString(false);
        if (xml != "<RSAKeyValue><Modulus>" + Convert.ToBase64String(X(RsaN)) + "</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>") return 118;
        string privateXml = rsa.ToXmlString(true);
        if (!privateXml.Contains("<D>" + Convert.ToBase64String(X(RsaD)) + "</D>", StringComparison.Ordinal)) return 119;
        if (!Throws<ArgumentNullException>(() => copy.FromXmlString(null!))) return 120;
#if !DESKTOP_ORACLE
        if (!Throws<PlatformNotSupportedException>(() => copy.FromXmlString(xml))) return 121;
#else
        copy.FromXmlString(xml);
        if (!Same(copy.ExportParameters(false).Modulus, X(RsaN))) return 121;
#endif

        // Hashing and signing route the digest through the subclass's SignHash.
        byte[] abc = Encoding.ASCII.GetBytes("abc");
        byte[] signature = rsa.SignData(abc, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (Convert.ToHexString(rsa.LastHash!) != "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD" || rsa.LastHashName != HashAlgorithmName.SHA256 || rsa.LastSignaturePadding != RSASignaturePadding.Pkcs1) return 122;
        if (signature.Length != 32 || signature[0] != (byte)(0xBA ^ 0xA5)) return 123;
        if (!rsa.VerifyData(abc, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return 124;
        if (!rsa.VerifyData(new MemoryStream(abc), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)) return 125;
        if (!Same(rsa.SignData(new byte[] { 9, 9, (byte)'a', (byte)'b', (byte)'c', 9 }, 2, 3, HashAlgorithmName.SHA256, RSASignaturePadding.Pss), signature)) return 126;
        if (rsa.LastSignaturePadding != RSASignaturePadding.Pss) return 127;
        if (rsa.TrySignData(abc, new byte[31], HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1, out written) || written != 0) return 128;
        byte[] sha1Signature = rsa.SignData(abc, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
        if (sha1Signature.Length != 20 || rsa.LastHashName != HashAlgorithmName.SHA1) return 129;
        if (!Throws<CryptographicException>(() => rsa.SignData(abc, new HashAlgorithmName("FOO"), RSASignaturePadding.Pkcs1))) return 130;
        if (!Throws<ArgumentException>(() => rsa.SignData(abc, new HashAlgorithmName(""), RSASignaturePadding.Pkcs1))) return 131;
        if (!Throws<ArgumentNullException>(() => rsa.SignData((byte[])null!, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))) return 132;
        byte[] encrypted = new byte[4];
        if (!rsa.TryEncrypt(new byte[] { 1, 2, 3 }, encrypted, RSAEncryptionPadding.OaepSHA256, out written) || written != 3 || encrypted[0] != 3) return 133;
        if (rsa.LastEncryptionPadding != RSAEncryptionPadding.OaepSHA256) return 134;
        if (!Throws<NotSupportedException>(() => rsa.EncryptValue(abc)) || !Throws<NotSupportedException>(() => rsa.DecryptValue(abc))) return 135;

        // Encrypted PKCS#8 validates its parameters before it needs a cipher. The round trip itself runs
        // OpenSSL's ciphers, which the NetWasm.App cryptography suite compares with desktop.
        if (!Throws<CryptographicException>(() => rsa.ExportEncryptedPkcs8PrivateKey("pw", new PbeParameters(PbeEncryptionAlgorithm.TripleDes3KeyPkcs12, HashAlgorithmName.SHA256, 2)))) return 136;
        return 0;
    }

    static int RsaAdapters()
    {
        using TestRsa rsa = new TestRsa();
        rsa.ImportParameters(RsaKey());
        byte[] secret = { 1, 2, 3, 4 };

        RSAOAEPKeyExchangeFormatter oaep = new RSAOAEPKeyExchangeFormatter(rsa);
        byte[] exchanged = oaep.CreateKeyExchange(secret);
        if (oaep.Parameters != null || exchanged[0] != 4 || rsa.LastEncryptionPadding != RSAEncryptionPadding.OaepSHA1) return 200;
        byte[] parameter = { 5 };
        oaep.Parameter = parameter;
        parameter[0] = 6;
        if (oaep.Parameter![0] != 5 || oaep.Rng != null) return 201;
        RSAOAEPKeyExchangeDeformatter oaepBack = new RSAOAEPKeyExchangeDeformatter(rsa);
        if (!Same(oaepBack.DecryptKeyExchange(exchanged), secret) || oaepBack.Parameters != null) return 202;

        RSAPKCS1KeyExchangeFormatter pkcs1 = new RSAPKCS1KeyExchangeFormatter();
        if (!Throws<CryptographicUnexpectedOperationException>(() => pkcs1.CreateKeyExchange(secret))) return 203;
        if (!Throws<ArgumentNullException>(() => pkcs1.SetKey(null!))) return 204;
        if (!Throws<InvalidCastException>(() => pkcs1.SetKey(new TestDsa()))) return 205;
        pkcs1.SetKey(rsa);
        if (!Same(pkcs1.CreateKeyExchange(secret, typeof(Aes)), new byte[] { 4, 3, 2, 1 }) || rsa.LastEncryptionPadding != RSAEncryptionPadding.Pkcs1) return 206;
        if (!pkcs1.Parameters.StartsWith("<enc:KeyEncryptionMethod enc:Algorithm=\"http://www.microsoft.com/xml/security/algorithm/PKCS1-v1.5-KeyEx\"", StringComparison.Ordinal)) return 207;
        RSAPKCS1KeyExchangeDeformatter pkcs1Back = new RSAPKCS1KeyExchangeDeformatter(rsa);
        if (!Same(pkcs1Back.DecryptKeyExchange(new byte[] { 4, 3, 2, 1 }), secret) || pkcs1Back.Parameters != null) return 208;

        byte[] hash = SHA256.HashData(Encoding.ASCII.GetBytes("abc"));
        RSAPKCS1SignatureFormatter signer = new RSAPKCS1SignatureFormatter(rsa);
        if (!Throws<CryptographicUnexpectedOperationException>(() => signer.CreateSignature(hash))) return 209;
        signer.SetHashAlgorithm("SHA256");
        byte[] signature = signer.CreateSignature(hash);
        if (rsa.LastHashName != HashAlgorithmName.SHA256 || rsa.LastSignaturePadding != RSASignaturePadding.Pkcs1) return 210;
        RSAPKCS1SignatureDeformatter verifier = new RSAPKCS1SignatureDeformatter(rsa);
        verifier.SetHashAlgorithm("SHA256");
        if (!verifier.VerifySignature(hash, signature) || verifier.VerifySignature(hash, new byte[32])) return 211;
        using SHA256 sha = SHA256.Create();
        sha.ComputeHash(Encoding.ASCII.GetBytes("abc"));
        if (!Same(signer.CreateSignature(sha), signature) || !verifier.VerifySignature(sha, signature)) return 212;
        if (!Throws<ArgumentNullException>(() => signer.CreateSignature((HashAlgorithm)null!))) return 213;
        HashOnlyDeformatter custom = new HashOnlyDeformatter();
        if (!custom.VerifySignature(sha, new byte[1]) || !Same(custom.Seen, hash)) return 214;

        RsaSignatureDescription description = new RsaSignatureDescription { DigestAlgorithm = "SHA256" };
        if (!Same(description.CreateFormatter(rsa).CreateSignature(hash), signature)) return 215;
        using HashAlgorithm? digest = description.CreateDigest();
        if (digest is null || digest.HashSize != 256) return 216;
        SecurityElement element = new SecurityElement("SignatureDescription");
        element.AddChild(new SecurityElement("Key", "System.Security.Cryptography.RSA"));
        element.AddChild(new SecurityElement("Digest", "SHA1"));
        element.AddChild(new SecurityElement("Formatter", "F"));
        element.AddChild(new SecurityElement("Deformatter", "D"));
        SignatureDescription fromElement = new SignatureDescription(element);
        if (fromElement.KeyAlgorithm != "System.Security.Cryptography.RSA" || fromElement.DigestAlgorithm != "SHA1" || fromElement.FormatterAlgorithm != "F" || fromElement.DeformatterAlgorithm != "D") return 217;
        if (new SignatureDescription().KeyAlgorithm != null || !Throws<ArgumentNullException>(() => new SignatureDescription(null!))) return 218;

        // CryptoConfig names the PKCS#1 formatter and deformatter, so a plain description creates them.
        SignatureDescription byName = new SignatureDescription
        {
            FormatterAlgorithm = "System.Security.Cryptography.RSAPKCS1SignatureFormatter",
            DeformatterAlgorithm = "System.Security.Cryptography.RSAPKCS1SignatureDeformatter",
        };
        AsymmetricSignatureFormatter namedSigner = byName.CreateFormatter(rsa);
        namedSigner.SetHashAlgorithm("SHA256");
        AsymmetricSignatureDeformatter namedVerifier = byName.CreateDeformatter(rsa);
        namedVerifier.SetHashAlgorithm("SHA256");
        if (namedSigner is not RSAPKCS1SignatureFormatter || !Same(namedSigner.CreateSignature(hash), signature) || !namedVerifier.VerifySignature(hash, signature)) return 219;
        if (CryptoConfig.CreateFromName("System.Security.Cryptography.RSAPKCS1SignatureDeformatter") is not RSAPKCS1SignatureDeformatter) return 220;
        return 0;
    }

    static int Dsa()
    {
        using TestDsa dsa = new TestDsa();
        dsa.ImportParameters(DsaKey());
        if (!Throws<NotImplementedException>(() => _ = dsa.SignatureAlgorithm) || dsa.KeySize != 1024) return 300;
        byte[] spki = dsa.ExportSubjectPublicKeyInfo();
        byte[] pkcs8 = dsa.ExportPkcs8PrivateKey();
        if (spki.Length != 443 || S(spki) != "B8F6C836B8025E54AE6933DB7C70CA87E06F4DED9D782F2CECF5E0B148B77085") return 301;
        if (pkcs8.Length != 334 || S(pkcs8) != "EFF2B3B3540B32BF3CF54B967F4D8DBEE09F31653458C56C185904B3659995BA") return 302;
        using TestDsa copy = new TestDsa();
        copy.ImportPkcs8PrivateKey(pkcs8, out int read);
        if (read != pkcs8.Length || !Same(copy.ExportParameters(true).X, X(DsaX)) || !Same(copy.ExportSubjectPublicKeyInfo(), spki)) return 303;
        if (dsa.ToXmlString(false) != "<DSAKeyValue><P>6yIe+L9gvMH+2N0lGUFDIxDQ9ZhpyqORPA4gZD3BtmZI2EIEinsWB91BWV3HyfuXjJ7+8jA0wdzYIsR8zLzZvKvGQWeIgteO220CGFQ5JeHcgK88J7IBVh7OjXKu9FSH0h55IsNGaY+4XcCKsDO+hI/inQZS6DndoGszt00R+XE=</P><Q>hxVqWwGGnFMKffOImtEZmxaAxlk=</Q><G>Z2oJnZ3Uw1uASwh/7YILty3C4lC4/TvDbm8C494lYN5n8lNk1H6Az8CMLhXkhQLJMmdWfygoYco+X+PLiB1M3pUjuVWAUWtL2La6lhuWIvPHaAhnl7HrHnxbjy5JJnhgkGkVCoLnYuixR+YrLMAqOKfSRlFRN2lu6s6pcfLHcow=</G><Y>zOnCoIn77du/znNkUumVouarpPGwmC4NMEjddMNYqajFVvS2BMMpeg/F1RWnnW6NFtFU1QUkcnxMl+SbUY/CyhEbvGcteMqAvlSde4T/ixU13GeiDX0nCweWJVOcsjIt1cdiYyYFhPqZQAVF4LUWyOuiSj2eIB8iRjqLv2hRm2I=</Y></DSAKeyValue>") return 304;

        byte[] abc = Encoding.ASCII.GetBytes("abc");
        byte[] signature = dsa.SignData(abc, HashAlgorithmName.SHA1);
        if (signature.Length != 40 || !Same(dsa.LastHash, SHA1.HashData(abc)) || !dsa.VerifyData(abc, signature, HashAlgorithmName.SHA1)) return 305;
        if (!dsa.VerifyData(new MemoryStream(abc), signature, HashAlgorithmName.SHA1) || dsa.VerifyData(abc, new byte[40], HashAlgorithmName.SHA1)) return 306;
        if (dsa.TryCreateSignature(SHA1.HashData(abc), new byte[39], out int written) || written != 0) return 307;

        DSASignatureFormatter formatter = new DSASignatureFormatter(dsa);
        formatter.SetHashAlgorithm("sha1");
        if (!Throws<CryptographicUnexpectedOperationException>(() => formatter.SetHashAlgorithm("SHA256"))) return 308;
        byte[] hash = SHA1.HashData(abc);
        DSASignatureDeformatter deformatter = new DSASignatureDeformatter(dsa);
        if (!deformatter.VerifySignature(hash, formatter.CreateSignature(hash))) return 309;
        if (!Throws<CryptographicUnexpectedOperationException>(() => new DSASignatureFormatter().CreateSignature(hash))) return 310;
        SignatureDescription dsaByName = new SignatureDescription
        {
            FormatterAlgorithm = "System.Security.Cryptography.DSASignatureFormatter",
            DeformatterAlgorithm = "System.Security.Cryptography.DSASignatureDeformatter",
        };
        AsymmetricSignatureFormatter dsaSigner = dsaByName.CreateFormatter(dsa);
        if (dsaSigner is not DSASignatureFormatter || !dsaByName.CreateDeformatter(dsa).VerifySignature(hash, dsaSigner.CreateSignature(hash))) return 312;
        return 0;
    }

    static int Ec()
    {
        using TestEcdsa ecdsa = new TestEcdsa();
        ecdsa.ImportParameters(EcKey());
        if (ecdsa.SignatureAlgorithm != "ECDsa" || ecdsa.KeyExchangeAlgorithm != null) return 400;
        byte[] spki = ecdsa.ExportSubjectPublicKeyInfo();
        byte[] priv = ecdsa.ExportECPrivateKey();
        byte[] pkcs8 = ecdsa.ExportPkcs8PrivateKey();
        if (spki.Length != 91 || S(spki) != "8126D65963C22731DCF7AB9FF9B2BAE8094FC36A8880728F70295C1CE4486C7E") return 401;
        if (priv.Length != 121 || S(priv) != "4D93CD702CD5B6F6B1524D44C7CC9AFDE365A36F8451F421DA67165DCB828CB1") return 402;
        if (pkcs8.Length != 138 || S(pkcs8) != "873C662EE56C5A7A3D569D3FD35CCFA44BFB776F5CA78F6475F85D78D8A84994") return 403;
        using TestEcdsa copy = new TestEcdsa();
        copy.ImportECPrivateKey(priv, out int read);
        ECParameters imported = copy.ExportParameters(true);
        if (read != priv.Length || !Same(imported.D, X(EcD)) || imported.Curve.Oid.Value != "1.2.840.10045.3.1.7") return 404;
        copy.ImportSubjectPublicKeyInfo(spki, out read);
        if (read != spki.Length || !Same(copy.ExportParameters(false).Q.Y, X(EcY))) return 405;
        copy.ImportPkcs8PrivateKey(pkcs8, out _);
        if (!Same(copy.ExportECPrivateKey(), priv)) return 406;
        if (!copy.ExportECPrivateKeyPem().StartsWith("-----BEGIN EC PRIVATE KEY-----\n", StringComparison.Ordinal)) return 407;
        if (!Throws<NotSupportedException>(() => ecdsa.ExportExplicitParameters(false)) || !Throws<NotSupportedException>(() => ecdsa.GenerateKey(ECCurve.NamedCurves.nistP256))) return 408;
        if (!Throws<NotImplementedException>(() => ecdsa.ToXmlString(false)) || !Throws<NotImplementedException>(() => ecdsa.FromXmlString("<x/>"))) return 409;

        byte[] abc = Encoding.ASCII.GetBytes("abc");
        byte[] p1363 = ecdsa.SignData(abc, HashAlgorithmName.SHA256);
        if (p1363.Length != 64 || Convert.ToHexString(ecdsa.LastHash!) != "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD" || !ecdsa.VerifyData(abc, p1363, HashAlgorithmName.SHA256)) return 410;
        byte[] der = ecdsa.SignData(abc, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        if (der[0] != 0x30 || der.Length < 70 || !ecdsa.VerifyData(abc, der, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) return 411;
        if (ecdsa.VerifyData(abc, p1363, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)) return 412;
        if (!ecdsa.VerifyHash(SHA256.HashData(abc), p1363) || ecdsa.TrySignHash(SHA256.HashData(abc), new byte[63], out int written) || written != 0) return 413;
        if (ecdsa.GetMaxSignatureSize(DSASignatureFormat.IeeeP1363FixedFieldConcatenation) != 64 || ecdsa.GetMaxSignatureSize(DSASignatureFormat.Rfc3279DerSequence) != 72) return 414;

        using TestEcdh ecdh = new TestEcdh();
        ecdh.ImportParameters(EcKey());
        if (ecdh.KeyExchangeAlgorithm != "ECDiffieHellman" || ecdh.SignatureAlgorithm != null) return 420;
        if (!Same(ecdh.ExportSubjectPublicKeyInfo(), spki) || !Same(ecdh.ExportECPrivateKey(), priv)) return 421;
        using ECDiffieHellmanPublicKey other = ecdh.PublicKey;
        if (!Same(ecdh.DeriveKeyFromHash(other, HashAlgorithmName.SHA256), SHA256.HashData(X(EcX)))) return 422;
        byte[] withPrepend = ecdh.DeriveKeyFromHash(other, HashAlgorithmName.SHA256, new byte[] { 1 }, new byte[] { 2 });
        byte[] input = new byte[34];
        input[0] = 1; X(EcX).CopyTo(input, 1); input[33] = 2;
        if (!Same(withPrepend, SHA256.HashData(input))) return 423;
        if (!Throws<NotImplementedException>(() => ecdh.DeriveKeyMaterial(other)) || !Throws<NotImplementedException>(() => ecdh.DeriveKeyFromHmac(other, HashAlgorithmName.SHA256, null))) return 424;
        if (!Throws<NotImplementedException>(() => ecdh.DeriveKeyTls(other, new byte[1], new byte[1]))) return 425;
        if (!Same(other.ExportParameters().Q.X, X(EcX)) || !Throws<NotImplementedException>(() => other.ToXmlString())) return 426;
        using BlobPublicKey blob = new BlobPublicKey(new byte[] { 1, 2 });
        byte[] blobBytes = blob.ToByteArray();
        blobBytes[0] = 9;
        if (blob.ToByteArray()[0] != 1 || !Throws<NotSupportedException>(() => blob.ExportParameters()) || !Throws<NotSupportedException>(() => blob.ExportExplicitParameters())) return 427;
        return 0;
    }

    static int AsnData()
    {
        byte[] raw = { 1, 2, 3 };
        AsnEncodedData data = new AsnEncodedData("1.2.3", raw);
        raw[0] = 9;
        if (data.Oid!.Value != "1.2.3" || data.RawData[0] != 1) return 500;
        // Format is OpenSSL's, as on Linux; the NetWasm.App cryptography suite compares it with desktop.
        AsnEncodedData noOid = new AsnEncodedData(new byte[] { 0xAB, 0x01 });
        if (noOid.Oid != null) return 501;
        AsnEncodedData copy = new AsnEncodedData(data);
        copy.CopyFrom(noOid);
        if (copy.Oid != null || copy.RawData.Length != 2) return 503;
        if (!Throws<ArgumentNullException>(() => copy.CopyFrom(null!)) || !Throws<ArgumentNullException>(() => copy.RawData = null!)) return 504;
        copy.Oid = new Oid("1.2.840.113549.1.1.1");
        if (copy.Oid.FriendlyName != "RSA") return 505;

        AsnEncodedDataCollection collection = new AsnEncodedDataCollection();
        if (collection.Add(data) != 0 || collection.Add(noOid) != 1 || collection.Count != 2 || collection[1] != noOid) return 506;
        if (!Throws<ArgumentNullException>(() => collection.Add(null!))) return 507;
        AsnEncodedData[] array = new AsnEncodedData[3];
        collection.CopyTo(array, 1);
        if (array[0] != null || array[1] != data || array[2] != noOid) return 508;
        System.Collections.ICollection untyped = collection;
        object[] objects = new object[2];
        untyped.CopyTo(objects, 0);
        if (objects[0] != data || untyped.IsSynchronized || untyped.SyncRoot != collection) return 509;
        AsnEncodedDataEnumerator enumerator = collection.GetEnumerator();
        int count = 0;
        while (enumerator.MoveNext()) count++;
        enumerator.Reset();
        if (count != 2 || !enumerator.MoveNext() || enumerator.Current != data) return 510;
        collection.Remove(data);
        if (collection.Count != 1 || collection[0] != noOid) return 511;
        if (new AsnEncodedDataCollection(data).Count != 1) return 512;
        return 0;
    }

    static int Bases()
    {
        BareAlgorithm bare = new BareAlgorithm();
        bare.KeySize = 768;
        if (bare.KeySize != 768 || !Throws<CryptographicException>(() => bare.KeySize = 300)) return 600;
        if (bare.LegalKeySizes[0].MaxSize != 1024) return 601;
        if (!Throws<NotImplementedException>(() => _ = bare.SignatureAlgorithm) || !Throws<NotImplementedException>(() => _ = bare.KeyExchangeAlgorithm)) return 602;
        if (!Throws<NotImplementedException>(() => bare.ExportSubjectPublicKeyInfo()) || !Throws<NotImplementedException>(() => bare.ExportPkcs8PrivateKey())) return 603;
        if (!Throws<NotImplementedException>(() => bare.ImportPkcs8PrivateKey(new byte[1], out _)) || !Throws<NotImplementedException>(() => bare.ToXmlString(false))) return 604;
        if (!Throws<NotImplementedException>(() => bare.ExportEncryptedPkcs8PrivateKey("pw", new PbeParameters(PbeEncryptionAlgorithm.Aes128Cbc, HashAlgorithmName.SHA256, 1)))) return 605;
        bare.Clear();
        if (!bare.Disposed) return 606;

        ICspAsymmetricAlgorithm csp = new CspAlgorithm();
        csp.ImportCspBlob(new byte[] { 3 });
        if (csp.ExportCspBlob(false)[0] != 3) return 607;
        if (!Throws<PlatformNotSupportedException>(() => new CspKeyContainerInfo(new CspParameters()))) return 608;
        return 0;
    }
}
