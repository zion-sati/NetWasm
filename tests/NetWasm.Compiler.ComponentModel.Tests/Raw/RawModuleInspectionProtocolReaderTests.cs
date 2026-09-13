using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawModuleInspectionProtocolReaderTests
{
    private const string Prefix = "{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",";

    [Fact]
    public void ReadsEveryNumericTypeInBinaryOrderAndPreservesEmptyIdentities()
    {
        var output = Prefix + "\"imports\":["
            + "{\"module\":\"host\",\"name\":\"first\",\"parameters\":[\"i32\",\"i64\",\"f32\",\"f64\"],\"results\":[\"f64\",\"f32\"]},"
            + "{\"module\":\"\",\"name\":\"\",\"parameters\":[],\"results\":[]}] }\n";

        var result = Create().Read(new(0, output.Replace("] }", "]}"), "local evidence"));

        Assert.Collection(result,
            first =>
            {
                Assert.Equal(new("host", "first"), first.Identity);
                Assert.Equal(
                    [RawCoreValueType.I32, RawCoreValueType.I64,
                        RawCoreValueType.F32, RawCoreValueType.F64],
                    first.Parameters);
                Assert.Equal([RawCoreValueType.F64, RawCoreValueType.F32], first.Results);
            },
            second =>
            {
                Assert.Equal(new("", ""), second.Identity);
                Assert.Empty(second.Parameters);
                Assert.Empty(second.Results);
            });
        Assert.False(result.IsDefault);
    }

    [Theory]
    [InlineData("invalid-arguments")]
    [InlineData("invalid-bytes")]
    [InlineData("invalid-core-module")]
    [InlineData("unsupported-import-kind")]
    [InlineData("unsupported-import-signature")]
    [InlineData("duplicate-import")]
    [InlineData("decoder-failure")]
    [InlineData("inconsistent-import-inventory")]
    [InlineData("inspection-command-failure")]
    public void ReportsOnlyKnownInspectionFailures(string code)
    {
        var exception = Assert.Throws<CompilerException>(() => Create().Read(new(
            1,
            Prefix + $"\"errorCode\":\"{code}\"}}\n",
            "private local detail")));

        Assert.Equal("NW1010", exception.Diagnostic.Id);
        Assert.Equal($"raw module inspection failed with '{code}'",
            exception.Diagnostic.Message);
        Assert.DoesNotContain("private local detail", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{}\r\n")]
    [InlineData("{}\n\n")]
    [InlineData(" {}\n")]
    [InlineData("{} \n")]
    [InlineData("{\n}\n")]
    [InlineData("{\r}\n")]
    [InlineData("not-json\n")]
    [InlineData("{invalid}\n")]
    [InlineData("[]\n")]
    public void RejectsNoncanonicalOrMalformedFraming(string output)
    {
        AssertInvalid(output, 0);
    }

    [Theory]
    [InlineData("{\"kind\":\"raw-module-import-signatures\",\"imports\":[]}")]
    [InlineData("{\"schemaVersion\":\"2\",\"kind\":\"raw-module-import-signatures\",\"imports\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"kind\":\"raw-module-import-signatures\",\"imports\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"imports\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"kind\":\"other\",\"imports\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"kind\":1,\"imports\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",\"imports\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",\"imports\":[],\"extra\":true}")]
    [InlineData("{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",\"errorCode\":\"invalid-bytes\"}")]
    public void RejectsSchemaKindMemberAndExitDriftOnSuccess(string payload)
    {
        AssertInvalid(payload + "\n", 0);
    }

    [Theory]
    [InlineData("{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",\"imports\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\"}")]
    [InlineData("{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",\"errorCode\":1}")]
    [InlineData("{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",\"errorCode\":\"unknown\"}")]
    [InlineData("{\"schemaVersion\":\"1\",\"kind\":\"raw-module-import-signatures\",\"errorCode\":\"invalid-bytes\",\"extra\":true}")]
    public void RejectsMalformedUnknownOrSuccessEnvelopesOnFailure(string payload)
    {
        AssertInvalid(payload + "\n", 1);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("1")]
    [InlineData("{\"module\":\"host\",\"name\":\"call\",\"parameters\":[],\"results\":[],\"extra\":true}")]
    [InlineData("{\"module\":\"host\",\"name\":\"call\",\"parameters\":[],\"parameters\":[],\"results\":[]}")]
    [InlineData("{\"module\":\"host\",\"name\":\"call\",\"results\":[],\"other\":[]}")]
    [InlineData("{\"name\":\"call\",\"parameters\":[],\"results\":[]}")]
    [InlineData("{\"module\":1,\"name\":\"call\",\"parameters\":[],\"results\":[]}")]
    [InlineData("{\"module\":\"host\",\"parameters\":[],\"results\":[]}")]
    [InlineData("{\"module\":\"host\",\"name\":null,\"parameters\":[],\"results\":[]}")]
    [InlineData("{\"module\":\"host\",\"name\":\"call\",\"results\":[]}")]
    [InlineData("{\"module\":\"host\",\"name\":\"call\",\"parameters\":1,\"results\":[]}")]
    [InlineData("{\"module\":\"host\",\"name\":\"call\",\"parameters\":[1],\"results\":[]}")]
    [InlineData("{\"module\":\"host\",\"name\":\"call\",\"parameters\":[\"v128\"],\"results\":[]}")]
    [InlineData("{\"module\":\"host\",\"name\":\"call\",\"parameters\":[]}")]
    [InlineData("{\"module\":\"host\",\"name\":\"call\",\"parameters\":[],\"results\":1}")]
    public void RejectsMalformedImportProducts(string import)
    {
        AssertInvalid(Prefix + $"\"imports\":[{import}]}}\n", 0);
    }

    [Fact]
    public void RejectsDuplicateImportIdentities()
    {
        const string Import = "{\"module\":\"host\",\"name\":\"call\",\"parameters\":[],\"results\":[]}";
        AssertInvalid(Prefix + $"\"imports\":[{Import},{Import}]}}\n", 0);
    }

    [Fact]
    public void RequiresAResult()
    {
        Assert.Throws<ArgumentNullException>(() => Create().Read(null!));
        AssertInvalid(null!, 0);
    }

    private static RawModuleInspectionProtocolReader Create() => new();

    private static void AssertInvalid(string output, int exitCode)
    {
        var exception = Assert.Throws<CompilerException>(() =>
            Create().Read(new(exitCode, output, "private local detail")));
        Assert.Equal("NW1010", exception.Diagnostic.Id);
        Assert.Equal(
            "raw module inspection command returned an invalid protocol envelope",
            exception.Diagnostic.Message);
        Assert.DoesNotContain("private local detail", exception.Diagnostic.Message,
            StringComparison.Ordinal);
    }
}
