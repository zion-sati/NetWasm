using System.Text.Json;
using NetWasm.Compiler.Cli.Tests.TestSupport;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.ExceptionTypes;
using NetWasm.Compiler.StackTraces;
using NetWasm.Compiler.Wasm;
using NetWasm.Compiler.Wasm.Emission.NativeInterop;

namespace NetWasm.Compiler.Cli.Tests;

public sealed class CompilerArtifactWriterTests
{
    [Fact]
    public void WritesEveryRequestedSidecarThenTheModule()
    {
        var files = new RecordingFiles();
        var compilation = CliTestData.Compilation(
            symbols: new StackTraceSymbolArtifact([7, 8], "application/json", "symbols.json", "digest"),
            entryPoint: new(ManagedExecutableParameterShape.StringArray, ManagedExecutableReturnShape.ExitCode),
            runtimeFeatures: [NetWasmRuntimeFeatureIds.LocalTime, NetWasmRuntimeFeatureIds.EphemeronHandles],
            functionImports: [new("host", "call", WasmFunctionType.Create(CliValueKind.I4, CliValueKind.ManagedAddress))],
            nativeImports:
            [
                new("mule", "compute", [WasmValueType.I32, WasmValueType.I64, WasmValueType.F32], WasmValueType.F64),
                new("__Internal", "reset", [], null),
            ]) with
        {
            ExceptionTypeMap = new ExceptionTypeMapArtifact([1, 2, 3], "application/json", "exceptions.json", "digest"),
        };

        CreateWriter(files).Write(new(compilation, "app.wasm", "app.callbacks.o")
        {
            RuntimeLayoutPath = "layout.json",
            InteropManifestPath = "interop.json",
            CompilerMetadataPath = "metadata.json",
            ExceptionTypeMapPath = "exceptions.json",
            StackTraceSymbolsPath = "symbols.json",
        });

        Assert.Equal(
            ["delete:app.callbacks.o", "text:layout.json", "text:interop.json", "binary:exceptions.json",
                "text:metadata.json", "binary:symbols.json", "binary:app.wasm"],
            files.Operations);
        Assert.Equal(compilation.CoreModule, files.BinaryWrites[^1].Content);
        Assert.Equal([1, 2, 3], files.BinaryWrites[0].Content);
        Assert.Equal([7, 8], files.BinaryWrites[1].Content);
        Assert.All(files.TextWrites, write => Assert.DoesNotContain('\r', write.Content));
        Assert.All(files.TextWrites, write => Assert.Contains('\n', write.Content));
        using var layout = Json(files, "layout.json");
        Assert.Equal(3, layout.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("wasm32", layout.RootElement.GetProperty("target").GetString());
        Assert.Equal(65_537, layout.RootElement.GetProperty("applicationStaticDataEnd").GetInt32());
        Assert.Equal(
            ["ephemeron-handles", "local-time"],
            layout.RootElement.GetProperty("runtimeFeatures").EnumerateArray().Select(value => value.GetString()));
        var entryPoint = layout.RootElement.GetProperty("managedExecutableEntryPoint");
        Assert.Equal("stringArray", entryPoint.GetProperty("parameterShape").GetString());
        Assert.Equal("exitCode", entryPoint.GetProperty("returnShape").GetString());
        var nativeImports = layout.RootElement.GetProperty("nativeImports");
        Assert.Equal("mule", nativeImports[0].GetProperty("libraryName").GetString());
        Assert.Equal("compute", nativeImports[0].GetProperty("entryPoint").GetString());
        Assert.Equal(
            ["i32", "i64", "f32"],
            nativeImports[0].GetProperty("parameters").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal("f64", nativeImports[0].GetProperty("returnType").GetString());
        Assert.Empty(nativeImports[1].GetProperty("parameters").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, nativeImports[1].GetProperty("returnType").ValueKind);
        Assert.Empty(layout.RootElement.GetProperty("importModules").EnumerateArray());
        Assert.False(layout.RootElement.TryGetProperty("nativeCallbackSupport", out _));
        using var interop = Json(files, "interop.json");
        Assert.Equal(1, interop.RootElement.GetProperty("version").GetInt32());
        Assert.Equal("wasm32", interop.RootElement.GetProperty("target").GetString());
        using var metadata = Json(files, "metadata.json");
        Assert.Equal(1, metadata.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(
            ["ephemeron-handles", "local-time"],
            metadata.RootElement.GetProperty("runtimeFeatures").EnumerateArray().Select(value => value.GetString()));
        var functionImport = Assert.Single(metadata.RootElement.GetProperty("functionImports").EnumerateArray());
        Assert.Equal("host", functionImport.GetProperty("module").GetString());
        Assert.Equal("call", functionImport.GetProperty("name").GetString());
        Assert.Equal(
            "managedAddress",
            Assert.Single(functionImport.GetProperty("parameters").EnumerateArray()).GetString());
        Assert.Equal("i4", functionImport.GetProperty("result").GetString());
    }

    [Fact]
    public void WritesOnlyTheModuleWhenNoSidecarIsRequested()
    {
        var files = new RecordingFiles();

        CreateWriter(files).Write(new(CliTestData.Compilation(), "app.wasm", "app.callbacks.o"));

        Assert.Equal(["delete:app.callbacks.o", "binary:app.wasm"], files.Operations);
    }

    [Fact]
    public void WritesEmptyListsForAbsentFacts()
    {
        var files = new RecordingFiles();
        var compilation = CliTestData.Compilation() with { RuntimeFeatures = default, FunctionImports = default };

        CreateWriter(files).Write(new(compilation, "app.wasm", "app.callbacks.o")
        {
            RuntimeLayoutPath = "layout.json",
            CompilerMetadataPath = "metadata.json",
        });

        using var layout = Json(files, "layout.json");
        Assert.Empty(layout.RootElement.GetProperty("runtimeFeatures").EnumerateArray());
        Assert.Empty(layout.RootElement.GetProperty("nativeImports").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, layout.RootElement.GetProperty("managedExecutableEntryPoint").ValueKind);
        using var metadata = Json(files, "metadata.json");
        Assert.Empty(metadata.RootElement.GetProperty("runtimeFeatures").EnumerateArray());
        Assert.Empty(metadata.RootElement.GetProperty("functionImports").EnumerateArray());
    }

    [Theory]
    [InlineData(WasmValueType.I32, "wasm32", "i32")]
    [InlineData(WasmValueType.I64, "wasm64", "i64")]
    public void WritesNativeCallbackSupportBeforeTheLayoutThatNamesIt(
        WasmValueType address, string target, string addressName)
    {
        var files = new RecordingFiles();
        var support = CliTestData.CallbackSupport(address, returnType: null);

        CreateWriter(files).Write(new(
            CliTestData.Compilation(target, nativeCallbackSupport: support),
            "out/app.wasm",
            "out/app.callbacks.o")
        {
            RuntimeLayoutPath = "layout.json",
        });

        Assert.Equal(["binary:out/app.callbacks.o", "text:layout.json", "binary:out/app.wasm"], files.Operations);
        Assert.Equal(support.ObjectBytes, files.BinaryWrites[0].Content);
        using var layout = Json(files, "layout.json");
        Assert.Equal(4, layout.RootElement.GetProperty("schemaVersion").GetInt32());
        var callbacks = layout.RootElement.GetProperty("nativeCallbackSupport");
        Assert.Equal("app.callbacks.o", callbacks.GetProperty("fileName").GetString());
        Assert.Equal(support.Sha256, callbacks.GetProperty("sha256").GetString());
        Assert.Equal(
            "application_callback",
            Assert.Single(callbacks.GetProperty("temporaryApplicationExports").EnumerateArray()).GetString());
        Assert.Equal(
            "callback_address",
            Assert.Single(callbacks.GetProperty("temporaryRuntimeExports").EnumerateArray()).GetString());
        var callback = Assert.Single(callbacks.GetProperty("callbacks").EnumerateArray());
        Assert.Equal("runtime_callback", callback.GetProperty("runtimeImportSymbol").GetString());
        Assert.Equal(addressName, callback.GetProperty("parameters")[1].GetString());
        Assert.Equal(JsonValueKind.Null, callback.GetProperty("returnType").ValueKind);
    }

    [Fact]
    public void RejectsUninitializedNativeFactsBeforeAnyWrite() =>
        AssertNoWrites<InvalidOperationException>(
            new(CliTestData.Compilation() with { NativeImports = default }, "app.wasm", "app.callbacks.o"),
            "The compiler native import facts are uninitialized.");

    [Fact]
    public void RejectsInvalidCallbackSupportBeforeAnyWrite()
    {
        var support = CliTestData.CallbackSupport(WasmValueType.I32) with { Sha256 = new string('0', 64) };

        AssertNoWrites<InvalidOperationException>(
            new(CliTestData.Compilation(nativeCallbackSupport: support), "app.wasm", "app.callbacks.o")
            {
                RuntimeLayoutPath = "layout.json",
            });
    }

    [Fact]
    public void RejectsCallbacksWithoutARuntimeLayoutBeforeAnyWrite() =>
        AssertNoWrites<CompilerException>(
            new(
                CliTestData.Compilation(nativeCallbackSupport: CliTestData.CallbackSupport(WasmValueType.I32)),
                "app.wasm",
                "app.callbacks.o"),
            "NW1000: native callbacks require -runtimelayout");

    [Theory]
    [InlineData("APP.WASM", null, null)]
    [InlineData(null, "App.Callbacks.O", null)]
    [InlineData(null, null, "layout.json")]
    public void RejectsOutputsThatDifferOnlyByCaseBeforeAnyWrite(
        string? layoutPath, string? metadataPath, string? symbolsPath) =>
        AssertNoWrites<CompilerException>(
            new(CliTestData.Compilation(), "app.wasm", "app.callbacks.o")
            {
                RuntimeLayoutPath = layoutPath ?? "layout.json",
                CompilerMetadataPath = metadataPath,
                StackTraceSymbolsPath = symbolsPath,
            },
            "NW1000: compiler output and sidecar paths must be distinct");

    [Fact]
    public void RejectsAMissingExceptionTypeMapBeforeAnyWrite() =>
        AssertNoWrites<InvalidOperationException>(
            new(CliTestData.Compilation(), "app.wasm", "app.callbacks.o") { ExceptionTypeMapPath = "exceptions.json" },
            "compiler diagnostics did not produce an exception type map");

    [Fact]
    public void RejectsMissingStackTraceSymbolsBeforeAnyWrite() =>
        AssertNoWrites<InvalidOperationException>(
            new(CliTestData.Compilation(), "app.wasm", "app.callbacks.o") { StackTraceSymbolsPath = "symbols.json" },
            "stack-trace instrumentation did not produce its symbol sidecar");

    [Fact]
    public void RejectsMissingInputs()
    {
        var files = new RecordingFiles();
        var validator = new NativeCallbackSupportArtifactValidator();

        Assert.Throws<ArgumentNullException>(() => CreateWriter(files).Write(null!));
        Assert.Throws<ArgumentNullException>(() => new CompilerArtifactWriter(null!, files, files, files));
        Assert.Throws<ArgumentNullException>(() => new CompilerArtifactWriter(validator, null!, files, files));
        Assert.Throws<ArgumentNullException>(() => new CompilerArtifactWriter(validator, files, null!, files));
        Assert.Throws<ArgumentNullException>(() => new CompilerArtifactWriter(validator, files, files, null!));
    }

    private static void AssertNoWrites<TException>(CompilerArtifactWriteRequest request, string? message = null)
        where TException : Exception
    {
        var files = new RecordingFiles();

        var exception = Assert.Throws<TException>(() => CreateWriter(files).Write(request));

        if (message is not null)
        {
            Assert.Equal(message, exception.Message);
        }
        Assert.Empty(files.Operations);
    }

    private static CompilerArtifactWriter CreateWriter(RecordingFiles files) =>
        new(new NativeCallbackSupportArtifactValidator(), files, files, files);

    private static JsonDocument Json(RecordingFiles files, string path) =>
        JsonDocument.Parse(files.TextWrites.Single(write => write.Path == path).Content);
}
