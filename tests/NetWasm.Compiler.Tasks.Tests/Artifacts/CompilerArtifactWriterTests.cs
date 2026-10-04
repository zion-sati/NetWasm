using System.Security.Cryptography;
using System.Text.Json;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Tasks.Artifacts;
using NetWasm.Compiler.Tasks.Tests.TestSupport;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission;

namespace NetWasm.Compiler.Tasks.Tests.Artifacts;

public sealed class CompilerArtifactWriterTests
{
    [Fact]
    public void WritePersistsTheCompilerArtifacts()
    {
        using var directory = new TestDirectory();
        var writer = Assert.IsAssignableFrom<ICompilerArtifactWriter>(
            CreateWriter());
        var compilation = CompilerTaskTestData.CreateCompilation(
            entryPoint: new(
                ManagedExecutableParameterShape.StringArray,
                ManagedExecutableReturnShape.ExitCode),
            runtimeFeatures: [NetWasmRuntimeFeatureIds.LocalTime],
            functionImports:
            [
                new("host", "call", WasmFunctionType.Create(
                    CliValueKind.I4,
                    CliValueKind.ManagedAddress)),
            ],
            nativeImports:
            [
                new("mule", "compute", [WasmValueType.I32, WasmValueType.I64, WasmValueType.F32], WasmValueType.F64),
                new("__Internal", "reset", [], null),
            ]) with
        {
            ExceptionTypeMap = new ExceptionTypeMapArtifact(
                [1, 2, 3],
                "application/vnd.netwasm.exception-types+json;version=2",
                "application.exceptions.json",
                new string('0', 64)),
        };
        var coreModulePath = directory.PathTo("application.core.wasm");
        var layoutPath = directory.PathTo("runtime-layout.json");
        var interopPath = directory.PathTo("interop.json");
        var compilerMetadataPath = directory.PathTo("compiler-metadata.json");

        var exceptionTypeMapPath = directory.PathTo("application.exceptions.json");
        writer.Write(new(
            compilation,
            coreModulePath,
            layoutPath,
            interopPath,
            compilerMetadataPath,
            directory.PathTo("application.callbacks.o"),
            null)
        {
            ExceptionTypeMapPath = exceptionTypeMapPath,
        });

        Assert.Equal(compilation.CoreModule, File.ReadAllBytes(coreModulePath));
        Assert.Equal(compilation.ExceptionTypeMap!.Bytes, File.ReadAllBytes(exceptionTypeMapPath));
        var layoutText = File.ReadAllText(layoutPath);
        Assert.Contains('\n', layoutText);
        Assert.DoesNotContain('\r', layoutText);
        using var layout = JsonDocument.Parse(layoutText);
        Assert.Equal(3, layout.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("wasm32", layout.RootElement.GetProperty("target").GetString());
        Assert.Equal(65_537, layout.RootElement.GetProperty("applicationStaticDataEnd").GetInt32());
        Assert.False(layout.RootElement.TryGetProperty("runtimeGlobalBase", out _));
        var nativeImports = layout.RootElement.GetProperty("nativeImports");
        Assert.Equal(2, nativeImports.GetArrayLength());
        Assert.Equal("mule", nativeImports[0].GetProperty("libraryName").GetString());
        Assert.Equal("compute", nativeImports[0].GetProperty("entryPoint").GetString());
        Assert.Equal(["i32", "i64", "f32"], nativeImports[0].GetProperty("parameters")
            .EnumerateArray().Select(value => value.GetString()!).ToArray());
        Assert.Equal("f64", nativeImports[0].GetProperty("returnType").GetString());
        Assert.Equal("__Internal", nativeImports[1].GetProperty("libraryName").GetString());
        Assert.Empty(nativeImports[1].GetProperty("parameters").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, nativeImports[1].GetProperty("returnType").ValueKind);
        var entryPoint = layout.RootElement.GetProperty("managedExecutableEntryPoint");
        Assert.Equal("stringArray", entryPoint.GetProperty("parameterShape").GetString());
        Assert.Equal("exitCode", entryPoint.GetProperty("returnShape").GetString());
        var interopText = File.ReadAllText(interopPath);
        Assert.Contains('\n', interopText);
        Assert.DoesNotContain('\r', interopText);
        using var interop = JsonDocument.Parse(interopText);
        Assert.Equal(1, interop.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("wasm32", interop.RootElement.GetProperty("target").GetString());
        using var compilerMetadata = JsonDocument.Parse(File.ReadAllText(compilerMetadataPath));
        Assert.Equal(1, compilerMetadata.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("wasm32", compilerMetadata.RootElement.GetProperty("target").GetString());
        Assert.Equal("local-time", Assert.Single(
            compilerMetadata.RootElement.GetProperty("runtimeFeatures").EnumerateArray()).GetString());
        var functionImport = Assert.Single(
            compilerMetadata.RootElement.GetProperty("functionImports").EnumerateArray());
        Assert.Equal("host", functionImport.GetProperty("module").GetString());
        Assert.Equal("call", functionImport.GetProperty("name").GetString());
        Assert.Equal("managedAddress", Assert.Single(
            functionImport.GetProperty("parameters").EnumerateArray()).GetString());
        Assert.Equal("i4", functionImport.GetProperty("result").GetString());
    }

    [Fact]
    public void WriteRejectsUninitializedNativeFactsBeforePublishingArtifacts()
    {
        using var directory = new TestDirectory();
        var core = directory.PathTo("application.core.wasm");
        var layout = directory.PathTo("runtime-layout.json");
        var interop = directory.PathTo("interop.json");
        var metadata = directory.PathTo("compiler-metadata.json");

        var error = Assert.Throws<InvalidOperationException>(() => CreateWriter().Write(new(
            CompilerTaskTestData.CreateCompilation() with { NativeImports = default },
            core, layout, interop, metadata,
            directory.PathTo("application.callbacks.o"), null)));

        Assert.Equal("The compiler native import facts are uninitialized.", error.Message);
        Assert.False(File.Exists(core));
        Assert.False(File.Exists(layout));
        Assert.False(File.Exists(interop));
        Assert.False(File.Exists(metadata));
    }

    [Fact]
    public void WriteNormalizesAbsentRuntimeFeaturesAndFunctionImportsWithoutInventingNativeCalls()
    {
        using var directory = new TestDirectory();
        var metadataPath = directory.PathTo("compiler-metadata.json");
        var layoutPath = directory.PathTo("runtime-layout.json");
        var compilation = CompilerTaskTestData.CreateCompilation() with
        {
            RuntimeFeatures = default,
            FunctionImports = default,
        };
        var writer = Assert.IsAssignableFrom<ICompilerArtifactWriter>(CreateWriter());

        writer.Write(new(compilation, directory.PathTo("application.core.wasm"),
            layoutPath, directory.PathTo("interop.json"), metadataPath,
            directory.PathTo("application.callbacks.o"), null));

        using var metadata = JsonDocument.Parse(File.ReadAllText(metadataPath));
        Assert.Empty(metadata.RootElement.GetProperty("runtimeFeatures").EnumerateArray());
        Assert.Empty(metadata.RootElement.GetProperty("functionImports").EnumerateArray());
        using var layout = JsonDocument.Parse(File.ReadAllText(layoutPath));
        Assert.Equal(3, layout.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Empty(layout.RootElement.GetProperty("nativeImports").EnumerateArray());
    }

    [Fact]
    public void WritePersistsTheRequestedStackTraceSymbols()
    {
        using var directory = new TestDirectory();
        var symbols = new StackTraceSymbolArtifact(
            [1, 2, 3],
            "application/json",
            "application.netwasm.stacktrace.json",
            "sha256");
        var writer = Assert.IsAssignableFrom<ICompilerArtifactWriter>(
            CreateWriter());
        var symbolsPath = directory.PathTo("symbols/application.stacktrace.json");

        writer.Write(new(
            CompilerTaskTestData.CreateCompilation(symbols: symbols),
            directory.PathTo("application.core.wasm"),
            directory.PathTo("runtime-layout.json"),
            directory.PathTo("interop.json"),
            directory.PathTo("compiler-metadata.json"),
            directory.PathTo("application.callbacks.o"),
            symbolsPath));

        Assert.Equal(symbols.Bytes, File.ReadAllBytes(symbolsPath));
    }

    [Fact]
    public void WriteOwnsVersionedCallbackSupportAndRemovesAStaleSidecar()
    {
        using var directory = new TestDirectory();
        var objectPath = directory.PathTo("application.callbacks.o");
        var layoutPath = directory.PathTo("runtime-layout.json");
        var bytes = new byte[] { 0, 97, 115, 109, 1, 0, 0, 0 };
        var digest = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var compilation = CompilerTaskTestData.CreateCompilation() with
        {
            NativeCallbackSupport = new(
                bytes,
                digest,
                [new(
                    "__netwasm_native_callback_0",
                    "__netwasm_native_callback_0",
                    "__netwasm_application_callback_0",
                    "__netwasm_callback_address_0",
                    [WasmValueType.I32, WasmValueType.I64],
                    WasmValueType.I32)],
                ["__netwasm_application_callback_0"],
                ["__netwasm_callback_address_0"]),
        };
        var writer = CreateWriter();

        writer.Write(new(
            compilation,
            directory.PathTo("application.core.wasm"),
            layoutPath,
            directory.PathTo("interop.json"),
            directory.PathTo("compiler-metadata.json"),
            objectPath,
            null));

        Assert.Equal(bytes, File.ReadAllBytes(objectPath));
        using (var layout = JsonDocument.Parse(File.ReadAllText(layoutPath)))
        {
            Assert.Equal(4, layout.RootElement.GetProperty("schemaVersion").GetInt32());
            var support = layout.RootElement.GetProperty("nativeCallbackSupport");
            Assert.Equal("application.callbacks.o", support.GetProperty("fileName").GetString());
            Assert.Equal(digest, support.GetProperty("sha256").GetString());
            var callback = Assert.Single(support.GetProperty("callbacks").EnumerateArray());
            Assert.Equal(
                "__netwasm_native_callback_0",
                callback.GetProperty("runtimeImportSymbol").GetString());
            Assert.Equal(
                "__netwasm_application_callback_0",
                Assert.Single(support.GetProperty("temporaryApplicationExports")
                    .EnumerateArray()).GetString());
            Assert.Equal(
                "__netwasm_callback_address_0",
                Assert.Single(support.GetProperty("temporaryRuntimeExports")
                    .EnumerateArray()).GetString());
        }

        writer.Write(new(
            CompilerTaskTestData.CreateCompilation(),
            directory.PathTo("application.core.wasm"),
            layoutPath,
            directory.PathTo("interop.json"),
            directory.PathTo("compiler-metadata.json"),
            objectPath,
            null));

        Assert.False(File.Exists(objectPath));
        using var noCallbacks = JsonDocument.Parse(File.ReadAllText(layoutPath));
        Assert.Equal(3, noCallbacks.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(noCallbacks.RootElement.TryGetProperty("nativeCallbackSupport", out _));
    }

    [Fact]
    public void WriteRejectsCallbackDigestMismatchBeforePublishingArtifacts()
    {
        using var directory = new TestDirectory();
        var compilation = CompilerTaskTestData.CreateCompilation() with
        {
            NativeCallbackSupport = new(
                [1],
                new string('0', 64),
                [new("native", "native", "application", "getter", [], null)],
                ["application"],
                ["getter"]),
        };
        var corePath = directory.PathTo("application.core.wasm");

        Assert.Throws<InvalidOperationException>(() =>
            CreateWriter().Write(new(
                compilation,
                corePath,
                directory.PathTo("runtime-layout.json"),
                directory.PathTo("interop.json"),
                directory.PathTo("compiler-metadata.json"),
                directory.PathTo("application.callbacks.o"),
                null)));

        Assert.False(File.Exists(corePath));
    }

    [Fact]
    public void WriteRejectsMissingRequestedStackTraceSymbols()
    {
        using var directory = new TestDirectory();
        var writer = Assert.IsAssignableFrom<ICompilerArtifactWriter>(
            CreateWriter());

        Assert.Throws<InvalidOperationException>(() => writer.Write(new(
            CompilerTaskTestData.CreateCompilation(),
            directory.PathTo("application.core.wasm"),
            directory.PathTo("runtime-layout.json"),
            directory.PathTo("interop.json"),
            directory.PathTo("compiler-metadata.json"),
            directory.PathTo("application.callbacks.o"),
            directory.PathTo("application.stacktrace.json"))));
    }

    [Fact]
    public void WriteRejectsANullRequest()
    {
        var writer = Assert.IsAssignableFrom<ICompilerArtifactWriter>(
            CreateWriter());

        Assert.Throws<ArgumentNullException>(() => writer.Write(null!));
    }

    private static CompilerArtifactWriter CreateWriter() => new(
        new NetWasm.Compiler.Wasm.Emission.NativeInterop.NativeCallbackSupportArtifactValidator());

    private sealed class TestDirectory : IDisposable
    {
        private readonly string _path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"netwasm-compiler-tasks-{Guid.NewGuid():N}");

        public TestDirectory() => Directory.CreateDirectory(_path);

        public string PathTo(string relativePath) => System.IO.Path.Combine(_path, relativePath);

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
