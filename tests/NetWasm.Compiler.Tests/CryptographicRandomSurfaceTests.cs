using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class CryptographicRandomSurfaceTests
{
    [Fact]
    public void DeclaresSupportedByteRangeAndSelectionSurface()
    {
        using var assets = TestAssets.Create();
        var compilation = CSharpCompilation.Create("RandomSurface",
            references: [MetadataReference.CreateFromFile(assets.CoreLib)]);
        var type = compilation.GetTypeByMetadataName("System.Security.Cryptography.RandomNumberGenerator");
        Assert.NotNull(type);
        Assert.True(type.IsAbstract);
        var methods = type.GetMembers().OfType<IMethodSymbol>()
            .Where(method => method.DeclaredAccessibility is Accessibility.Public or Accessibility.Protected);
        Assert.Equal(ExpectedMembers.Order(), methods.Select(method => method.GetDocumentationCommentId()).Order());
    }

    [Fact]
    public void CustomArrayGeneratorDoesNotRetainTheSecureProvider()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("CustomRandomRetention", """
            using System;
            using System.Security.Cryptography;
            public sealed class FixedRandom : RandomNumberGenerator
            {
                public override void GetBytes(byte[] data) => Array.Fill(data, (byte)42);
            }
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    using RandomNumberGenerator random = new FixedRandom();
                    Span<byte> bytes = stackalloc byte[1];
                    random.GetBytes(bytes);
                    return bytes[0];
                }
                public static int Unused() => RandomNumberGenerator.GetInt32(10);
            }
            """);
        // No WIT world is selected: an accidentally retained secure import fails compilation.
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", []));
        Assert.DoesNotContain(result.Program.WitImportMethods, method =>
            method.WitImport?.InterfaceName == "wasi:random/random@0.2.11");
        Assert.DoesNotContain(result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values), body =>
            body.Method.DeclaringType.FullName == "System.Security.Cryptography.RandomNumberGeneratorImplementation");
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 0, WasmTarget.Wasm32, result.StaticDataEnd));
    }

    [Fact]
    public void FillRetainsOnlyItsByteProviderPath()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("StaticRandomRetention", """
            using System;
            using System.Security.Cryptography;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    Span<byte> bytes = stackalloc byte[1];
                    RandomNumberGenerator.Fill(bytes);
                    return input;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        Assert.Contains(result.Program.WitImportMethods, method => method.WitImport is
        {
            InterfaceName: "wasi:random/random@0.2.11",
            FunctionName: "get-random-bytes",
        });
        var names = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method)
            .Where(method => method.DeclaringType.FullName?.StartsWith("System.Security.Cryptography.RandomNumberGenerator", StringComparison.Ordinal) == true)
            .Select(method => method.Definition.Name).Distinct().Order();
        Assert.Equal(FillMembers, names);
    }

    [Fact]
    public void RandomFileNameRetainsTheExistingSecureByteProvider()
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("RandomFileNameRetention", """
            using System.IO;
            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var name = Path.GetRandomFileName();
                    return name.Length == 12 && name[8] == '.' ? input : 0;
                }
            }
            """);
        var result = NetWasmCompiler.Compile(new(
            assembly,
            [assets.CoreLib],
            "EntryPoint",
            "Run",
            [],
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));

        Assert.Contains(result.Program.WitImportMethods, method => method.WitImport is
        {
            InterfaceName: "wasi:random/random@0.2.11",
            FunctionName: "get-random-bytes",
        });
        Assert.Contains(result.Program.Methods.Values, body =>
            body.Method.Definition.Name == "Populate83FileNameFromRandomBytes");
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
    }

    private static readonly string[] FillMembers = ["Fill", "FillSpan", "GetBytes"];

    // Pinned upstream byte, range, item, string, hex and shuffle surface.
    // Create(string) is present but rejects named algorithm lookup at runtime.
    private static readonly string[] ExpectedMembers =
    [
        "M:System.Security.Cryptography.RandomNumberGenerator.#ctor",
        "M:System.Security.Cryptography.RandomNumberGenerator.Create",
        "M:System.Security.Cryptography.RandomNumberGenerator.Create(System.String)",
        "M:System.Security.Cryptography.RandomNumberGenerator.Dispose",
        "M:System.Security.Cryptography.RandomNumberGenerator.Dispose(System.Boolean)",
        "M:System.Security.Cryptography.RandomNumberGenerator.Fill(System.Span{System.Byte})",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetBytes(System.Byte[])",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetBytes(System.Byte[],System.Int32,System.Int32)",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetBytes(System.Int32)",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetBytes(System.Span{System.Byte})",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetInt32(System.Int32)",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetInt32(System.Int32,System.Int32)",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetItems``1(System.ReadOnlySpan{``0},System.Span{``0})",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetItems``1(System.ReadOnlySpan{``0},System.Int32)",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetString(System.ReadOnlySpan{System.Char},System.Int32)",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetHexString(System.Span{System.Char},System.Boolean)",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetHexString(System.Int32,System.Boolean)",
        "M:System.Security.Cryptography.RandomNumberGenerator.Shuffle``1(System.Span{``0})",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetNonZeroBytes(System.Byte[])",
        "M:System.Security.Cryptography.RandomNumberGenerator.GetNonZeroBytes(System.Span{System.Byte})",
    ];
}
