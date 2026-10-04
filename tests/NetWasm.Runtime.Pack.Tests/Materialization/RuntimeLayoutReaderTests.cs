using System.Text.Json;
using System.Text.Json.Nodes;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeLayoutReaderTests
{
    private static readonly string[] ApplicationCallbackExports = ["application"];
    private static readonly string[] RuntimeCallbackExports = ["getter"];

    [Fact]
    public void ReadsCompilerRuntimeLayoutEvidence()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Write("runtime-layout.json", JsonSerializer.Serialize(new
        {
            schemaVersion = 2,
            target = "wasm64",
            applicationStaticDataEnd = 65_537,
        }));

        var reader = Assert.IsAssignableFrom<IRuntimeLayoutReader>(new RuntimeLayoutReader());
        var layout = reader.Read(path);

        Assert.Equal(2, layout.SchemaVersion);
        Assert.Equal("wasm64", layout.Target);
        Assert.Equal(65_537, layout.ApplicationStaticDataEnd);
        Assert.Empty(layout.NativeImports);
        Assert.True(layout.RuntimeFeatures.IsDefault);
    }

    [Fact]
    public void ReadsCanonicalRuntimeFeaturesAndDistinguishesAnExplicitEmptySet()
    {
        using var directory = new TemporaryDirectory();
        var selectedPath = directory.Write("selected.json", """
            {"schemaVersion":3,"target":"wasm32","applicationStaticDataEnd":0,
             "runtimeFeatures":["ephemeron-handles","structured-command-diagnostics"],
             "nativeImports":[]}
            """);
        var emptyPath = directory.Write("empty.json", """
            {"schemaVersion":3,"target":"wasm32","applicationStaticDataEnd":0,
             "runtimeFeatures":[],"nativeImports":[]}
            """);

        var reader = new RuntimeLayoutReader();
        Assert.Equal(
            ["ephemeron-handles", "structured-command-diagnostics"],
            reader.Read(selectedPath).RuntimeFeatures.ToArray());
        Assert.Empty(reader.Read(emptyPath).RuntimeFeatures);
        Assert.False(reader.Read(emptyPath).RuntimeFeatures.IsDefault);
    }

    [Theory]
    [InlineData("[\"structured-command-diagnostics\",\"ephemeron-handles\"]")]
    [InlineData("[\"ephemeron-handles\",\"ephemeron-handles\"]")]
    [InlineData("[\"future-feature\"]")]
    public void RejectsNonCanonicalRuntimeFeatures(string features)
    {
        using var directory = new TemporaryDirectory();
        var layout = $$"""
            {"schemaVersion":3,"target":"wasm32","applicationStaticDataEnd":0,
             "runtimeFeatures":{{features}},"nativeImports":[]}
            """;

        Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write("layout.json", layout)));
    }

    [Fact]
    public void ReadsExplicitPhysicalNativeSignaturesAndVoidReturn()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Write("layout.json", """
            {"schemaVersion":3,"target":"wasm64","applicationStaticDataEnd":123,
             "nativeImports":[
               {"libraryName":"mule","entryPoint":"compute","parameters":["i32","i64","f32","f64"],"returnType":"i64"},
               {"libraryName":"__Internal","entryPoint":"reset","parameters":[],"returnType":null}]}
            """);

        var layout = new RuntimeLayoutReader().Read(path);

        Assert.Equal(3, layout.SchemaVersion);
        Assert.Equal(2, layout.NativeImports.Length);
        var first = layout.NativeImports[0];
        Assert.Equal("mule", first.LibraryName);
        Assert.Equal("compute", first.EntryPoint);
        Assert.Equal([RuntimeNativeValueType.I32, RuntimeNativeValueType.I64, RuntimeNativeValueType.F32,
            RuntimeNativeValueType.F64], first.Parameters.ToArray());
        Assert.Equal(RuntimeNativeValueType.I64, first.ReturnType);
        Assert.Equal("__Internal", layout.NativeImports[1].LibraryName);
        Assert.Equal("reset", layout.NativeImports[1].EntryPoint);
        Assert.Empty(layout.NativeImports[1].Parameters);
        Assert.Null(layout.NativeImports[1].ReturnType);
    }

    [Fact]
    public void ReadsExactCompilerOwnedCallbackSupport()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Write("layout.json", """
            {"schemaVersion":4,"target":"wasm64","applicationStaticDataEnd":123,
             "nativeImports":[],
             "nativeCallbackSupport":{
               "fileName":"application.callbacks.o",
               "sha256":"0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
               "callbacks":[{"nativeSymbol":"native_0","runtimeImportSymbol":"runtime_0",
                 "applicationExportName":"application_0",
                 "runtimeGetterExportName":"getter_0","parameters":["i32","i64"],"returnType":"f64"}],
               "temporaryApplicationExports":["application_0"],
               "temporaryRuntimeExports":["getter_0"]}}
            """);

        var layout = new RuntimeLayoutReader().Read(path);

        Assert.Equal(4, layout.SchemaVersion);
        var support = Assert.IsType<RuntimeNativeCallbackSupport>(layout.NativeCallbackSupport);
        Assert.Equal("application.callbacks.o", support.FileName);
        Assert.Equal(
            "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            support.Sha256);
        var callback = Assert.Single(support.Callbacks);
        Assert.Equal("native_0", callback.NativeSymbol);
        Assert.Equal("runtime_0", callback.RuntimeImportSymbol);
        Assert.Equal("application_0", callback.ApplicationExportName);
        Assert.Equal("getter_0", callback.RuntimeGetterExportName);
        Assert.Equal(
            [RuntimeNativeValueType.I32, RuntimeNativeValueType.I64],
            callback.Parameters.ToArray());
        Assert.Equal(RuntimeNativeValueType.F64, callback.ReturnType);
        Assert.Equal(["application_0"], support.TemporaryApplicationExports.ToArray());
        Assert.Equal(["getter_0"], support.TemporaryRuntimeExports.ToArray());
    }

    [Theory]
    [InlineData("../callback.o", "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("callback.o", "ABCDEF6789abcdef0123456789abcdef0123456789abcdef0123456789abcdef")]
    [InlineData("callback.o", "short")]
    public void RejectsInvalidCallbackSupportIdentity(string fileName, string digest)
    {
        using var directory = new TemporaryDirectory();
        var content = JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            target = "wasm32",
            applicationStaticDataEnd = 0,
            nativeImports = Array.Empty<object>(),
            nativeCallbackSupport = new
            {
                fileName,
                sha256 = digest,
                callbacks = new[]
                {
                    new
                    {
                        nativeSymbol = "native",
                        runtimeImportSymbol = "runtime",
                        applicationExportName = "application",
                        runtimeGetterExportName = "getter",
                        parameters = Array.Empty<string>(),
                        returnType = (string?)null,
                    },
                },
                temporaryApplicationExports = ApplicationCallbackExports,
                temporaryRuntimeExports = RuntimeCallbackExports,
            },
        });

        Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write("layout.json", content)));
    }

    [Fact]
    public void RejectsInvalidOrCollidingCallbackSymbols()
    {
        using var directory = new TemporaryDirectory();
        var invalidName = CallbackLayout([
            new
            {
                nativeSymbol = "native",
                runtimeImportSymbol = " ",
                applicationExportName = "application",
                runtimeGetterExportName = "getter",
                parameters = Array.Empty<string>(),
                returnType = (string?)null,
            },
        ], ["application"], ["getter"]);
        Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write(
                "invalid-name.json",
                invalidName)));

        var collidingLinkerSymbol = CallbackLayout([
            new
            {
                nativeSymbol = "native_1",
                runtimeImportSymbol = "native_1",
                applicationExportName = "application_1",
                runtimeGetterExportName = "getter_1",
                parameters = Array.Empty<string>(),
                returnType = (string?)null,
            },
            new
            {
                nativeSymbol = "native_2",
                runtimeImportSymbol = "native_1",
                applicationExportName = "application_2",
                runtimeGetterExportName = "getter_2",
                parameters = Array.Empty<string>(),
                returnType = (string?)null,
            },
        ], ["application_1", "application_2"], ["getter_1", "getter_2"]);
        Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write(
                "colliding-linker-symbol.json",
                collidingLinkerSymbol)));

        var collidingApplicationExport = CallbackLayout([
            new
            {
                nativeSymbol = "native_1",
                runtimeImportSymbol = "runtime_1",
                applicationExportName = "application",
                runtimeGetterExportName = "getter_1",
                parameters = Array.Empty<string>(),
                returnType = (string?)null,
            },
            new
            {
                nativeSymbol = "native_2",
                runtimeImportSymbol = "runtime_2",
                applicationExportName = "application",
                runtimeGetterExportName = "getter_2",
                parameters = Array.Empty<string>(),
                returnType = (string?)null,
            },
        ], ["application", "application"], ["getter_1", "getter_2"]);
        Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write(
                "colliding-application-export.json",
                collidingApplicationExport)));
    }

    [Fact]
    public void RejectsCallbackSupportOutsideSchemaFour()
    {
        using var directory = new TemporaryDirectory();
        var root = JsonNode.Parse(ValidCallbackLayout())!.AsObject();
        root["schemaVersion"] = 3;

        Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write(
                "schema-three-callback.json",
                root.ToJsonString())));
    }

    [Theory]
    [InlineData("missing-field")]
    [InlineData("file-name-kind")]
    [InlineData("digest-kind")]
    [InlineData("callbacks-kind")]
    [InlineData("application-exports-kind")]
    [InlineData("runtime-exports-kind")]
    [InlineData("callback-missing-field")]
    [InlineData("callback-parameters-kind")]
    public void RejectsMalformedCallbackContractShapes(string defect)
    {
        using var directory = new TemporaryDirectory();
        var root = JsonNode.Parse(ValidCallbackLayout())!.AsObject();
        var support = root["nativeCallbackSupport"]!.AsObject();
        var callback = support["callbacks"]!.AsArray()[0]!.AsObject();
        switch (defect)
        {
            case "missing-field":
                support.Remove("fileName");
                break;
            case "file-name-kind":
                support["fileName"] = 0;
                break;
            case "digest-kind":
                support["sha256"] = 0;
                break;
            case "callbacks-kind":
                support["callbacks"] = new JsonObject();
                break;
            case "application-exports-kind":
                support["temporaryApplicationExports"] = new JsonObject();
                break;
            case "runtime-exports-kind":
                support["temporaryRuntimeExports"] = new JsonObject();
                break;
            case "callback-missing-field":
                callback.Remove("nativeSymbol");
                break;
            case "callback-parameters-kind":
                callback["parameters"] = new JsonObject();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(defect));
        }

        Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write(
                defect + ".json",
                root.ToJsonString())));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void ReadsExplicitEmptyNativeImports(int schema)
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Write("layout.json", JsonSerializer.Serialize(new
        {
            schemaVersion = schema,
            target = "wasm32",
            applicationStaticDataEnd = 0,
            nativeImports = Array.Empty<object>(),
        }));

        Assert.Empty(new RuntimeLayoutReader().Read(path).NativeImports);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":3,\"target\":\"wasm32\",\"applicationStaticDataEnd\":0}")]
    [InlineData("{\"schemaVersion\":3,\"target\":\"wasm32\",\"nativeImports\":[]}")]
    [InlineData("{\"schemaVersion\":3,\"applicationStaticDataEnd\":0,\"nativeImports\":[]}")]
    [InlineData("{\"schemaVersion\":3,\"target\":\"wasm32\",\"applicationStaticDataEnd\":0,\"nativeImports\":null}")]
    [InlineData("{\"schemaVersion\":3,\"target\":\"wasm32\",\"applicationStaticDataEnd\":0,\"nativeImports\":{}}")]
    [InlineData("{\"schemaVersion\":3,\"SchemaVersion\":2,\"target\":\"wasm32\",\"applicationStaticDataEnd\":0,\"nativeImports\":[]}")]
    [InlineData("{\"schemaVersion\":3,\"target\":\"wasm32\",\"target\":\"wasm64\",\"applicationStaticDataEnd\":0,\"nativeImports\":[]}")]
    [InlineData("{\"schemaVersion\":3,\"target\":\"wasm32\",\"applicationStaticDataEnd\":0,\"nativeImports\":[],\"NativeImports\":[]}")]
    [InlineData("{\"schemaVersion\":\"3\",\"target\":\"wasm32\",\"applicationStaticDataEnd\":0,\"nativeImports\":[]}")]
    public void RejectsIncompleteOrContradictorySchemaThreeContracts(string content)
    {
        using var directory = new TemporaryDirectory();
        var error = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write("layout.json", content)));
        Assert.Equal("The NetWasm runtime layout evidence is invalid.", error.Message);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"libraryName\":\"mule\",\"entryPoint\":\"compute\",\"parameters\":[]}")]
    [InlineData("{\"libraryName\":\"mule\",\"entryPoint\":\"compute\",\"returnType\":null}")]
    [InlineData("{\"libraryName\":\"mule\",\"entryPoint\":\"compute\",\"parameters\":null,\"returnType\":null}")]
    [InlineData("{\"libraryName\":\"mule\",\"entryPoint\":\"compute\",\"parameters\":[],\"returnType\":null,\"ReturnType\":\"i32\"}")]
    [InlineData("{\"libraryName\":\"mule\",\"LibraryName\":\"other\",\"entryPoint\":\"compute\",\"parameters\":[],\"returnType\":null}")]
    [InlineData("{\"libraryName\":\" \",\"entryPoint\":\"compute\",\"parameters\":[],\"returnType\":null}")]
    [InlineData("{\"libraryName\":\"mule\",\"entryPoint\":null,\"parameters\":[],\"returnType\":null}")]
    public void RejectsIncompleteOrContradictoryNativeImportRecords(string import)
    {
        using var directory = new TemporaryDirectory();
        var content = "{\"schemaVersion\":3,\"target\":\"wasm32\",\"applicationStaticDataEnd\":0,\"nativeImports\":[" + import + "]}";
        var error = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write("layout.json", content)));
        Assert.Equal("The NetWasm runtime layout evidence is invalid.", error.Message);
    }

    [Theory]
    [InlineData("[\"v128\"]", "null")]
    [InlineData("[0]", "null")]
    [InlineData("[]", "\"nativeInt\"")]
    [InlineData("[]", "123")]
    public void RejectsUnqualifiedPhysicalValueTypes(string parameters, string result)
    {
        using var directory = new TemporaryDirectory();
        var content = "{\"schemaVersion\":3,\"target\":\"wasm32\",\"applicationStaticDataEnd\":0,\"nativeImports\":[{\"libraryName\":\"mule\",\"entryPoint\":\"compute\",\"parameters\":" + parameters + ",\"returnType\":" + result + "}]}";
        var error = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write("layout.json", content)));
        Assert.Equal("The NetWasm runtime layout evidence is malformed.", error.Message);
    }

    [Fact]
    public void RejectsNativeImportsInLegacySchema()
    {
        using var directory = new TemporaryDirectory();
        var path = directory.Write("layout.json", """
            {"schemaVersion":2,"target":"wasm32","applicationStaticDataEnd":0,
             "nativeImports":[{"libraryName":"mule","entryPoint":"reset","parameters":[],"returnType":null}]}
            """);
        var error = Assert.Throws<InvalidOperationException>(() => new RuntimeLayoutReader().Read(path));
        Assert.Equal("The NetWasm runtime layout evidence is invalid.", error.Message);
    }

    [Fact]
    public void RejectsMissingLayoutEvidence()
    {
        using var directory = new TemporaryDirectory();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.PathTo("missing.json")));
        Assert.Equal("The NetWasm runtime layout evidence is missing.", exception.Message);
    }

    [Fact]
    public void RejectsMalformedLayoutEvidence()
    {
        using var directory = new TemporaryDirectory();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write("layout.json", "{")));
        Assert.Equal("The NetWasm runtime layout evidence is malformed.", exception.Message);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1,\"target\":\"wasm32\",\"applicationStaticDataEnd\":0}")]
    [InlineData("{\"schemaVersion\":1,\"target\":\"wasm32\",\"applicationStaticDataEnd\":0,\"nativeImports\":[]}")]
    [InlineData("{\"schemaVersion\":4,\"target\":\"wasm64\",\"applicationStaticDataEnd\":0,\"nativeImports\":[]}")]
    [InlineData("{\"schemaVersion\":2,\"target\":\" \",\"applicationStaticDataEnd\":0}")]
    [InlineData("{\"schemaVersion\":2,\"target\":\"wasm32\",\"applicationStaticDataEnd\":-1}")]
    public void RejectsInvalidLayoutEvidence(string content)
    {
        using var directory = new TemporaryDirectory();
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeLayoutReader().Read(directory.Write("layout.json", content)));
        Assert.Equal("The NetWasm runtime layout evidence is invalid.", exception.Message);
    }

    [Fact]
    public void RejectsMissingPath() =>
        Assert.Throws<ArgumentException>(() => new RuntimeLayoutReader().Read(" "));

    private static string CallbackLayout(
        object[] callbacks,
        string[] temporaryApplicationExports,
        string[] temporaryRuntimeExports) => JsonSerializer.Serialize(new
        {
            schemaVersion = 4,
            target = "wasm32",
            applicationStaticDataEnd = 0,
            nativeImports = Array.Empty<object>(),
            nativeCallbackSupport = new
            {
                fileName = "application.callbacks.o",
                sha256 =
                    "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
                callbacks,
                temporaryApplicationExports,
                temporaryRuntimeExports,
            },
        });

    private static string ValidCallbackLayout() => CallbackLayout([
        new
        {
            nativeSymbol = "native",
            runtimeImportSymbol = "runtime",
            applicationExportName = "application",
            runtimeGetterExportName = "getter",
            parameters = Array.Empty<string>(),
            returnType = (string?)null,
        },
    ], ApplicationCallbackExports, RuntimeCallbackExports);
}
