using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Cryptography's native half (OpenSSL and upstream's System.Security.Cryptography.Native shim) stays out of a program
// that does not use it. Hashing, HMAC, PBKDF2, random numbers and OID names are managed, so a program that uses
// only those imports none of the shim's entry points; one that encrypts, signs or reads a certificate imports the ones
// it reaches. The harness does not link native archives, so each native call a program reaches is an import of the
// emitted module, named after its entry point.
public sealed class CryptographyPayForUseCompilationTests
{
    [Theory]
    [InlineData("Hashes")]
    [InlineData("Authenticates")]
    [InlineData("DerivesKeys")]
    [InlineData("DrawsRandomBytes")]
    [InlineData("NamesOids")]
    [InlineData("AsksForSupport")]
    public void ManagedCryptographyImportsNoNativeEntryPoint(string method)
    {
        var imports = Compile(method);

        Assert.DoesNotContain(imports, IsShimEntryPoint);
    }

    [Theory]
    [InlineData("Encrypts", "CryptoNative_EvpCipherCreate2")]
    [InlineData("Signs", "CryptoNative_EcDsaSignHash")]
    [InlineData("ReadsCertificates", "CryptoNative_DecodeX509")]
    public void NativeCryptographyImportsWhatItReaches(string method, string entryPoint)
    {
        var imports = Compile(method);

        Assert.Contains(entryPoint, imports);
        Assert.Contains("CryptoNative_EnsureOpenSslInitialized", imports);
        Assert.Contains("NetWasmCryptoNative_SetRandomSource", imports);
        Assert.Contains("NetWasmCryptoNative_SetClock", imports);
    }

    private static bool IsShimEntryPoint(string name) =>
        name.StartsWith("CryptoNative_", StringComparison.Ordinal) ||
        name.StartsWith("NetWasmCryptoNative_", StringComparison.Ordinal);

    private static IReadOnlySet<string> Compile(string method)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileOptimizedSource("CryptographyPayForUseFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "CryptographyPayForUseFixture.EntryPoint",
            method, [new("run", "CryptographyPayForUseFixture.EntryPoint", method)], Target: WasmTarget.Wasm32,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        return WasmModuleInspection.ReadFunctionImportNames(result.ApplicationModule);
    }

    private const string Source = """
        using System;
        using System.Security.Cryptography;
        using System.Security.Cryptography.X509Certificates;
        using System.Text;
        namespace CryptographyPayForUseFixture;

        public static class EntryPoint
        {
            public static int Hashes(int input) =>
                SHA256.HashData(BitConverter.GetBytes(input))[0] + SHA512.HashData(new byte[1])[0] + input;

            public static int Authenticates(int input) =>
                HMACSHA256.HashData(new byte[16], BitConverter.GetBytes(input))[0] + input;

            public static int DerivesKeys(int input) =>
                Rfc2898DeriveBytes.Pbkdf2("pw", new byte[16], 2, HashAlgorithmName.SHA256, 16)[0] + input;

            public static int DrawsRandomBytes(int input) => RandomNumberGenerator.GetInt32(input + 1);

            public static int NamesOids(int input) =>
                new Oid("2.16.840.1.101.3.4.2.1").FriendlyName!.Length + Oid.FromFriendlyName("sha256", OidGroup.HashAlgorithm).Value!.Length + input;

            public static int AsksForSupport(int input) =>
                (AesGcm.IsSupported ? 1 : 0) + (AesCcm.IsSupported ? 1 : 0) + (ChaCha20Poly1305.IsSupported ? 1 : 0) + input;

            public static int Encrypts(int input)
            {
                using Aes aes = Aes.Create();
                aes.Key = new byte[16];
                return aes.EncryptEcb(new byte[16], PaddingMode.None)[0] + input;
            }

            public static int Signs(int input)
            {
                using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                return key.SignData(BitConverter.GetBytes(input), HashAlgorithmName.SHA256).Length + input;
            }

            public static int ReadsCertificates(int input)
            {
                using X509Certificate2 certificate = new X509Certificate2(new byte[input]);
                return certificate.Subject.Length;
            }
        }
        """;
}
