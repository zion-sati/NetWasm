// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// CryptographicOperations' HMAC one-shots and asynchronous hashes, .NET 9's algorithm-named forms of the HMAC classes'
// static methods: RFC 4231's first SHA-256 case, the same MAC from spans, arrays and streams, synchronously and
// asynchronously, verification, destination sizes and SHA3's absence. TUnit calls TryHmacData.
public sealed class CryptographicHmacOneShotCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32)]
    [InlineData(WasmTarget.Wasm64)]
    public void HmacOneShotsMatchTheHmacClasses(WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("HmacOneShotFixture", Source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "HmacOneShotFixture.EntryPoint",
            "Run", [new("run", "HmacOneShotFixture.EntryPoint", "Run")], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 41, target, result.StaticDataEnd));
    }

    private const string Source = """
        using System;
        using System.IO;
        using System.Security.Cryptography;
        using System.Text;
        namespace HmacOneShotFixture;

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                var sha256 = HashAlgorithmName.SHA256;
                var key = new byte[20];
                key.AsSpan().Fill(0x0b);
                var data = Encoding.ASCII.GetBytes("Hi There");
                var expected = Convert.FromHexString("B0344C61D8DB38535CA8AFCEAF0BF12B881DC200C9833DA726E9376C2E32CFF7");
                if (!CryptographicOperations.HmacData(sha256, key, data).AsSpan().SequenceEqual(expected)) return 1;
                if (!CryptographicOperations.HmacData(sha256, key.AsSpan(), data.AsSpan()).AsSpan().SequenceEqual(HMACSHA256.HashData(key, data))) return 2;
                Span<byte> destination = stackalloc byte[32];
                if (CryptographicOperations.HmacData(sha256, key, data, destination) != 32 || !destination.SequenceEqual(expected)) return 3;
                if (CryptographicOperations.TryHmacData(sha256, key, data, stackalloc byte[31], out var written) || written != 0) return 4;
                if (!CryptographicOperations.TryHmacData(sha256, key, data, destination, out written) || written != 32) return 5;
                if (!CryptographicOperations.HmacData(sha256, key, new MemoryStream(data)).AsSpan().SequenceEqual(expected)) return 6;
                if (!CryptographicOperations.VerifyHmac(sha256, key, data, expected)) return 7;
                var wrong = (byte[])expected.Clone();
                wrong[0] ^= 1;
                if (CryptographicOperations.VerifyHmac(sha256, key, data, wrong)) return 8;
                if (!CryptographicOperations.VerifyHmac(sha256, key, new MemoryStream(data), expected)) return 9;
                var asyncMac = CryptographicOperations.HmacDataAsync(sha256, key, new MemoryStream(data));
                if (!asyncMac.IsCompletedSuccessfully || !asyncMac.Result.AsSpan().SequenceEqual(expected)) return 10;
                var verified = CryptographicOperations.VerifyHmacAsync(sha256, key, new MemoryStream(data), expected);
                if (!verified.IsCompletedSuccessfully || !verified.Result) return 11;
                var hash = CryptographicOperations.HashDataAsync(sha256, new MemoryStream(data));
                if (!hash.IsCompletedSuccessfully || !hash.Result.AsSpan().SequenceEqual(SHA256.HashData(data))) return 12;
                try { CryptographicOperations.HmacData(sha256, key, data, stackalloc byte[16]); return 13; }
                catch (ArgumentException ex) when (ex.ParamName == "destination") { }
                try { CryptographicOperations.HmacData(sha256, null!, data); return 14; }
                catch (ArgumentNullException) { }
                try { CryptographicOperations.HmacData(HashAlgorithmName.SHA3_256, key, data); return 15; }
                catch (PlatformNotSupportedException) { }
                try { CryptographicOperations.HmacData(new HashAlgorithmName("unknown"), key, data); return 16; }
                catch (CryptographicException) { }
                return input + 1;
            }
        }
        """;
}
