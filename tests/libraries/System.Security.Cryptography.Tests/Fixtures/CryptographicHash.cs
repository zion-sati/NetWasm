// From the compiler harness's CryptographicHashCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
#pragma warning disable SYSLIB0021, SYSLIB0022, SYSLIB0023, SYSLIB0041, SYSLIB0045, SYSLIB0060, CS0618
using System;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Text;
namespace HashFixture;
sealed class NativeBuffer : SafeBuffer
{
    public NativeBuffer(int size, bool initialize) : base(true)
    {
        SetHandle(Marshal.AllocHGlobal(size));
        if (initialize) Initialize((ulong)size);
    }
    protected override bool ReleaseHandle()
    {
        Marshal.FreeHGlobal(handle);
        return true;
    }
}
public static class EntryPoint
{
    const string Two = "abcdbcdecdefdefgefghfghighijhijkijkljklmklmnlmnomnopnopq";
    const string Long = "abcdefghbcdefghicdefghijdefghijkefghijklfghijklmghijklmnhijklmnoijklmnopjklmnopqklmnopqrlmnopqrsmnopqrstnopqrstu";
    static string Hex(byte[] value)
    {
        var text = new StringBuilder(value.Length * 2);
        foreach (byte b in value) text.Append(b.ToString("x2"));
        return text.ToString();
    }
    static byte[] Ascii(string value) => Encoding.ASCII.GetBytes(value);
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    public static int Run(int input)
    {
        if (Hex(SHA1.Create().ComputeHash(Ascii("abc"))) != "a9993e364706816aba3e25717850c26c9cd0d89d") return 1;
        if (Hex(SHA256.Create().ComputeHash(Ascii("abc"))) != "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad") return 2;
        if (Hex(SHA384.Create().ComputeHash(Ascii("abc"))) != "cb00753f45a35e8bb5a03d699ac65007272c32ab0eded1631a8b605a43ff5bed8086072ba1e7cc2358baeca134c825a7") return 3;
        if (Hex(SHA512.Create().ComputeHash(Ascii("abc"))) != "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f") return 4;
        if (Hex(new SHA256Managed().ComputeHash(Array.Empty<byte>())) != "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855") return 5;
        if (Hex(new SHA1CryptoServiceProvider().ComputeHash(Ascii(Two))) != "84983e441c3bd26ebaae4aa1f95129e5e54670f1") return 6;
        if (Hex(new SHA256CryptoServiceProvider().ComputeHash(Ascii(Two))) != "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1") return 7;
        if (Hex(new SHA384Managed().ComputeHash(Ascii(Long))) != "09330c33f71147e83d192fc782cd1b4753111b173b3b05d22fa08086e3b0f712fcc7c71a557e2db966c3e9fa91746039") return 8;
        if (Hex(new SHA512CryptoServiceProvider().ComputeHash(Ascii(Long))) != "8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909") return 9;

        // Chunked transforms, buffered streams, reuse and span boundaries match one-shot output.
        byte[] two = Ascii(Two);
        using (var sha = SHA256.Create())
        {
            if (sha.HashSize != 256 || sha.InputBlockSize != 1 || !sha.CanReuseTransform || !sha.CanTransformMultipleBlocks) return 10;
            var copy = new byte[two.Length];
            int written = 0;
            for (int offset = 0; offset < 50; offset += 7) written += sha.TransformBlock(two, offset, Math.Min(7, 50 - offset), copy, offset);
            if (written != 50 || copy[49] != two[49]) return 11;
            if (!Throws<CryptographicUnexpectedOperationException>(() => _ = sha.Hash)) return 12;
            byte[] tail = sha.TransformFinalBlock(two, 50, two.Length - 50);
            if (tail.Length != two.Length - 50 || Hex(sha.Hash!) != "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1") return 13;
            if (Hex(sha.ComputeHash(new MemoryStream(two))) != Hex(sha.ComputeHash(two))) return 14;
            if (Hex(sha.ComputeHash(two, 3, 10)) != Hex(SHA256.HashData(new ReadOnlySpan<byte>(two, 3, 10)))) return 15;
            var small = new byte[31];
            if (sha.TryComputeHash(two, small, out int none) || none != 0) return 16;
            var exact = new byte[32];
            if (!sha.TryComputeHash(two, exact, out int all) || all != 32 || Hex(exact) != "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1") return 17;
        }
        using (var incremental = IncrementalHash.CreateHash(HashAlgorithmName.SHA512))
        {
            byte[] longBytes = Ascii(Long);
            for (int i = 0; i < longBytes.Length; i += 13) incremental.AppendData(longBytes, i, Math.Min(13, longBytes.Length - i));
            if (Hex(incremental.GetHashAndReset()) != "8e959b75dae313da8cf4f72814fc143f8f7779c6eb9f7fa17299aeadb6889018501d289e4900f7e4331b99dec4b5433ac7d329eeb6dd26545e96e55b874be909") return 18;
            incremental.AppendData(Ascii("abc"));
            if (Hex(incremental.GetHashAndReset()) != "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f") return 19;
            if (incremental.AlgorithmName != HashAlgorithmName.SHA512 || incremental.HashLengthInBytes != 64) return 20;
        }
        var disposed = SHA1.Create();
        disposed.Dispose();
        if (!Throws<ObjectDisposedException>(() => disposed.ComputeHash(two))) return 21;

        // HMAC known answers, including a key longer than the block size.
        byte[] jefe = Ascii("Jefe"), want = Ascii("what do ya want for nothing?");
        if (Hex(new HMACSHA1(jefe).ComputeHash(want)) != "effcdf6ae5eb2fa2d27416d5f184df9c259a7c79") return 22;
        if (Hex(new HMACSHA256(jefe).ComputeHash(want)) != "5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843") return 23;
        if (Hex(new HMACSHA384(jefe).ComputeHash(want)) != "af45d2e376484031617f78d2b58a6b1b9c7ef464f5a01b47e42ec3736322445e8e2240ca5e69e2c78b3239ecfab21649") return 24;
        if (Hex(new HMACSHA512(jefe).ComputeHash(want)) != "164b7a7bfcf819e2e395fbe73b56e0a387bd64222e831fd610270cd7ea2505549758bf75c05a994a6d034f65f8f0e6fdcaeab1a34d4a6b4b636e070a38bce737") return 25;
        var bigKey = new byte[131];
        Array.Fill(bigKey, (byte)0xaa);
        byte[] bigData = Ascii("Test Using Larger Than Block-Size Key - Hash Key First");
        if (Hex(new HMACSHA256(bigKey).ComputeHash(bigData)) != "60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54") return 26;
        using (var mac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, bigKey))
        {
            mac.AppendData(bigData, 0, 10);
            mac.AppendData(bigData, 10, bigData.Length - 10);
            if (Hex(mac.GetHashAndReset()) != "60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54") return 27;
        }
        var keyed = new HMACSHA256(jefe);
        byte[] keyCopy = keyed.Key;
        keyCopy[0] = 0;
        if (keyed.Key[0] != (byte)'J' || keyed.HashName != "SHA256" || keyed.HashSize != 256) return 28;
        keyed.Key = bigKey;
        if (Hex(keyed.ComputeHash(bigData)) != "60e431591ee0b67f0d8a26aacbf5b77f8e0bc6213728c5140546040f0ee37f54") return 29;

        // PBKDF2 known answers and multi-call output.
        byte[] password = Ascii("password"), salt = Ascii("salt");
        using (var kdf = new Rfc2898DeriveBytes(password, salt, 1, HashAlgorithmName.SHA1))
            if (Hex(kdf.GetBytes(20)) != "0c60c80f961f0e71f3a9b524af6012062fe037a6") return 30;
        using (var kdf = new Rfc2898DeriveBytes("password", salt, 4096, HashAlgorithmName.SHA1))
            if (Hex(kdf.GetBytes(20)) != "4b007901b765489abead49d926f721d065a429c1") return 31;
        if (Hex(Rfc2898DeriveBytes.Pbkdf2(password, salt, 2, HashAlgorithmName.SHA256, 32)) != "ae4d0c95af6b46d32d0adff928f06dd02a303f8ef3c251dfd6e2d85a95474c43") return 32;
        using (var whole = new Rfc2898DeriveBytes(password, salt, 3, HashAlgorithmName.SHA256))
        using (var split = new Rfc2898DeriveBytes(password, salt, 3, HashAlgorithmName.SHA256))
        {
            string expected = Hex(whole.GetBytes(45));
            string parts = Hex(split.GetBytes(10)) + Hex(split.GetBytes(25)) + Hex(split.GetBytes(10));
            if (parts != expected || split.IterationCount != 3 || Hex(split.Salt) != Hex(salt)) return 33;
            split.Reset();
            if (Hex(split.GetBytes(45)) != expected) return 34;
        }
        if (!Throws<ArgumentOutOfRangeException>(() => new Rfc2898DeriveBytes(password, salt, 0))) return 35;
        if (!Throws<CryptographicException>(() => Rfc2898DeriveBytes.Pbkdf2(password, salt, 1, new HashAlgorithmName("unknown"), 16))) return 36;
        // Desktop .NET 10 vectors for the legacy PBKDF1 extension.
        using (var legacy = new PasswordDeriveBytes(password, salt, "SHA256", 100))
        {
            if (Hex(legacy.GetBytes(16)) + Hex(legacy.GetBytes(40)) != "a391a781211f503622f91a988ba34e98d2597f2ef414fcc1a87e69889f96d2dcca08381eed301b18e1d7865088f9f01566857efd18a9959c") return 67;
            if (legacy.HashName != "SHA256" || legacy.IterationCount != 100 || !Throws<CryptographicException>(() => legacy.HashName = "SHA1")) return 68;
        }
        using (var legacy = new PasswordDeriveBytes("password", salt))
            if (Hex(legacy.GetBytes(24)) != "8333f080d0f0bf68768fbcb8b81fccbdb745ec6c24a6f3c4") return 69;
        using (var legacy = new PasswordDeriveBytes("password", salt))
        using (var kdf = new Rfc2898DeriveBytes(password, salt, 2, HashAlgorithmName.SHA256))
            if (!Throws<PlatformNotSupportedException>(() => legacy.CryptDeriveKey("RC2", "SHA1", 128, new byte[8])) || !Throws<PlatformNotSupportedException>(() => kdf.CryptDeriveKey("RC2", "SHA1", 128, new byte[8]))) return 74;

        // Bounded name resolution, randomness and deferred algorithms.
        if (!(CryptoConfig.CreateFromName("SHA256") is SHA256Managed) || !(CryptoConfig.CreateFromName("SHA-512") is SHA512Managed)) return 38;
        if (!(CryptoConfig.CreateFromName("System.Security.Cryptography.KeyedHashAlgorithm") is HMACSHA1)) return 39;
        if (CryptoConfig.CreateFromName("Nope") != null) return 40;
        // Names match ignoring case, as desktop's table does, and MD5 resolves.
        if (!(CryptoConfig.CreateFromName("sha256") is SHA256) || !(CryptoConfig.CreateFromName("MD5") is MD5) ||
            !(CryptoConfig.CreateFromName("hmacmd5") is HMACMD5)) return 65;
#if !DESKTOP_ORACLE
        // NetWasm boundaries: the bounded table creates only managed algorithms, and SHA-3 and runtime type
        // registration are unavailable.
        if (!Throws<PlatformNotSupportedException>(() => CryptoConfig.AddAlgorithm(typeof(object), "x"))) return 64;
        if (!Throws<PlatformNotSupportedException>(() => Rfc2898DeriveBytes.Pbkdf2(password, salt, 1, new HashAlgorithmName("SHA3-256"), 16))) return 37;
#endif
        if (!(HashAlgorithm.Create("SHA384") is SHA384Managed) || !(HMAC.Create("HMACSHA512") is HMACSHA512) || !(RandomNumberGenerator.Create("RandomNumberGenerator") is RNGCryptoServiceProvider)) return 41;
        if (CryptoConfig.AllowOnlyFipsAlgorithms) return 66;
        if (Hex(CryptoConfig.EncodeOID("1.2.840.113549.1.1.1")) != "06092a864886f70d010101") return 43;
        if (!Throws<ArgumentNullException>(() => CryptoConfig.CreateFromName(null!))) return 44;
        if (!Throws<PlatformNotSupportedException>(() => HashAlgorithm.Create())) return 47;
        using (var rng = new RNGCryptoServiceProvider())
        {
            var bytes = new byte[256];
            rng.GetNonZeroBytes(bytes);
            foreach (byte b in bytes) if (b == 0) return 48;
            var a = new byte[32]; var b2 = new byte[32];
            rng.GetBytes(a); rng.GetBytes(b2);
            if (Hex(a) == Hex(b2)) return 49;
        }
        if (!Throws<PlatformNotSupportedException>(() => new RNGCryptoServiceProvider(new CspParameters()))) return 50;

        // Values, adapters and memory operations.
        if (HashAlgorithmName.MD5.Name != "MD5" || HashAlgorithmName.SHA1 != new HashAlgorithmName("SHA1") || HashAlgorithmName.SHA1 == HashAlgorithmName.SHA256 || default(HashAlgorithmName).Name != null || HashAlgorithmName.SHA384.ToString() != "SHA384") return 51;
        var csp = new CspParameters();
        if (csp.KeyNumber != -1 || csp.ProviderType != 24 || csp.ProviderName != null || csp.Flags != CspProviderFlags.NoFlags) return 52;
        if (!Throws<ArgumentException>(() => csp.Flags = (CspProviderFlags)0x40000)) return 53;
        using (var sha = SHA256.Create())
        using (var crypto = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write))
        {
            crypto.Write(two, 0, 20);
            crypto.Write(two, 20, two.Length - 20);
            crypto.FlushFinalBlock();
            if (!crypto.HasFlushedFinalBlock || Hex(sha.Hash!) != "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1") return 54;
            if (!Throws<NotSupportedException>(() => crypto.FlushFinalBlock())) return 55;
        }
        using (var sha = SHA256.Create())
        using (var reader = new CryptoStream(new MemoryStream(two), sha, CryptoStreamMode.Read, leaveOpen: false))
        {
            var sink = new byte[9];
            int total = 0, read;
            while ((read = reader.Read(sink, 0, sink.Length)) > 0) total += read;
            if (total != two.Length || !reader.CanRead || reader.CanWrite || Hex(sha.Hash!) != "248d6a61d20638b8e5c026930c3e6039a33ce45964ff2167f6ecedd419db06c1") return 70;
        }
        using (var native = new NativeBuffer(16, true))
        {
            native.Write<int>(4, 0x01020304);
            native.WriteArray<short>(8, new short[] { 1, 2, 3, 4 }, 0, 4);
            var back = new short[4];
            native.ReadArray<short>(8, back, 0, 4);
            if (native.ByteLength != 16 || native.Read<int>(4) != 0x01020304 || back[0] != 1 || back[3] != 4) return 71;
            if (!Throws<ArgumentException>(() => native.Write<long>(12, 1L)) || !Throws<ArgumentOutOfRangeException>(() => native.ReadArray<short>(8, back, -1, 1))) return 72;
        }
        using (var uninitialized = new NativeBuffer(8, false))
            if (!Throws<InvalidOperationException>(() => uninitialized.Read<int>(0))) return 73;
        if (!CryptographicOperations.FixedTimeEquals(two, Ascii(Two)) || CryptographicOperations.FixedTimeEquals(two, Ascii("abc")) || CryptographicOperations.FixedTimeEquals(Ascii("abd"), Ascii("abc"))) return 56;
        var secret = Ascii("secret");
        CryptographicOperations.ZeroMemory(secret);
        foreach (byte b in secret) if (b != 0) return 57;
        var mask = new PKCS1MaskGenerationMethod();
        if (mask.HashName != "SHA1" || mask.GenerateMask(Ascii("seed"), 30).Length != 30 || Hex(mask.GenerateMask(Ascii("seed"), 30)) != Hex(mask.GenerateMask(Ascii("seed"), 30))) return 58;

        using (var secure = new SecureString())
        {
            foreach (char c in "pass") secure.AppendChar(c);
            secure.InsertAt(0, 'x'); secure.SetAt(1, 'P'); secure.RemoveAt(4);
            if (secure.Length != 4 || secure.IsReadOnly()) return 59;
            using SecureString copy = secure.Copy();
            secure.MakeReadOnly();
            if (!secure.IsReadOnly() || !Throws<InvalidOperationException>(() => secure.AppendChar('z'))) return 60;
            copy.AppendChar('z');
            if (copy.Length != 5 || secure.Length != 4 || copy.IsReadOnly()) return 61;
            if (!Throws<ArgumentOutOfRangeException>(() => copy.SetAt(5, 'q'))) return 62;
        }
        var gone = new SecureString();
        gone.Dispose();
        if (!Throws<ObjectDisposedException>(() => _ = gone.Length)) return 63;
        // MD5 (RFC 1321's suite) and HMAC-MD5 (RFC 2104): padding at each block boundary, many blocks, incremental.
        if (Hex(MD5.Create().ComputeHash(Array.Empty<byte>())) != "d41d8cd98f00b204e9800998ecf8427e" ||
            Hex(MD5.HashData(Ascii("abc"))) != "900150983cd24fb0d6963f7d28e17f72" ||
            Hex(MD5.HashData(Ascii("message digest"))) != "f96b697d7cb7938d525a2f31aaf161d0") return 75;
        if (Hex(MD5.HashData(new string('a', 55).Select(c => (byte)c).ToArray())) != "ef1772b6dff9a122358552954ad0df65" ||
            Hex(MD5.HashData(new string('a', 56).Select(c => (byte)c).ToArray())) != "3b0c8ac703f828b04c6c197006d17218" ||
            Hex(MD5.HashData(new string('a', 63).Select(c => (byte)c).ToArray())) != "b06521f39153d618550606be297466d5" ||
            Hex(MD5.HashData(new string('a', 64).Select(c => (byte)c).ToArray())) != "014842d480b571495a4a0363793f7367" ||
            Hex(MD5.HashData(new string('a', 65).Select(c => (byte)c).ToArray())) != "c743a45e0d2e6a95cb859adae0248435") return 76;
        if (Hex(new MD5CryptoServiceProvider().ComputeHash(Ascii("12345678901234567890123456789012345678901234567890123456789012345678901234567890"))) != "57edf4a22be3c955ac49da2e2107b67a") return 77;
        using (var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5))
        {
            md5.AppendData(Ascii("message "));
            md5.AppendData(Ascii("digest"));
            if (Hex(md5.GetHashAndReset()) != "f96b697d7cb7938d525a2f31aaf161d0" || md5.HashLengthInBytes != 16) return 78;
        }
        if (Hex(new HMACMD5(jefe).ComputeHash(want)) != "750c783e6ab0b503eaa86e310a5db738" || Hex(HMACMD5.HashData(jefe, want)) != "750c783e6ab0b503eaa86e310a5db738") return 79;
        return input + 1;
    }
}
