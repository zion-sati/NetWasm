using System.Text.Json;
using System.Text.Json.Nodes;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusCaseManifestParserTests
{
    private const string Valid = """
        {
          "schemaVersion": 1,
          "caseId": "numeric.boundaries-1",
          "featureIds": ["S06"],
          "inputKind": "CSharp",
          "sourceFiles": ["numeric/Entry.cs.txt", "numeric/Support.cs"],
          "name": "NumericBoundaries",
          "namespace": "NetWasm.Correctness.Numeric",
          "entryMethod": "Run",
          "inputs": [-2147483648, 2147483647],
          "expectations": [
            { "input": -2147483648, "returnValue": 2147483647, "exceptionType": null },
            { "input": 2147483647, "returnValue": null, "exceptionType": "System.OverflowException" }
          ],
          "referencePaths": ["references/Support.dll"],
          "oracleMode": "SameIl",
          "matrixProfile": "Extended",
          "testMethod": "NetWasm.Compiler.Tests.NumericTests.Boundaries"
        }
        """;

    [Fact]
    public void PreservesExactInt32BoundariesAndAllDeclaredIdentity()
    {
        var manifest = Parser().Parse(Valid);

        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal("numeric.boundaries-1", manifest.CaseId);
        Assert.Equal<string>(["S06"], manifest.FeatureIds);
        Assert.Equal(CorpusInputKind.CSharp, manifest.InputKind);
        Assert.Equal<string>(["numeric/Entry.cs.txt", "numeric/Support.cs"], manifest.SourceFiles);
        Assert.Equal("NumericBoundaries", manifest.Name);
        Assert.Equal("NetWasm.Correctness.Numeric", manifest.Namespace);
        Assert.Equal("Run", manifest.EntryMethod);
        Assert.Null(manifest.DesktopEntryMethod);
        Assert.Equal<int>([int.MinValue, int.MaxValue], manifest.Inputs);
        Assert.Equal(new CorpusCaseExpectation(int.MinValue, int.MaxValue, null), manifest.Expectations[0]);
        Assert.Equal(new CorpusCaseExpectation(int.MaxValue, null, "System.OverflowException"), manifest.Expectations[1]);
        Assert.Equal<string>(["references/Support.dll"], manifest.ReferencePaths);
        Assert.Equal(OracleMode.SameIl, manifest.OracleMode);
        Assert.Equal(CorpusMatrixProfile.Extended, manifest.MatrixProfile);
        Assert.Equal("NetWasm.Compiler.Tests.NumericTests.Boundaries", manifest.TestMethod);
        Assert.Null(manifest.SameSourceReason);
        Assert.Null(manifest.ExecutionBackend);
        Assert.Empty(manifest.ReferenceAssemblyAliases);
        Assert.Equal(OracleRuntimeCapabilities.None, manifest.RequiredRuntimeCapabilities);
        Assert.False(manifest.AllowUnsafe);
        Assert.False(manifest.RequiresReactor);
        Assert.False(manifest.SupportsBatchedOracle);
        Assert.False(manifest.ReportAllMismatches);
        Assert.True(manifest.ExposesLegacyTrace);
        Assert.False(manifest.UsesTypedTrace);
    }

    [Fact]
    public void PreservesOptionalSettingsAndDoesNotPerformSemanticValidation()
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        json["sameSourceReason"] = "  Exact declared reason.  ";
        json["desktopEntryMethod"] = "ExactOracle";
        json["executionBackend"] = "Linked";
        json["requiredRuntimeCapabilities"] = "GarbageCollection, Finalization, WeakReferenceClearing";
        json["allowUnsafe"] = true;
        json["requiresReactor"] = true;
        json["supportsBatchedOracle"] = true;
        json["reportAllMismatches"] = true;
        json["exposesLegacyTrace"] = false;
        json["usesTypedTrace"] = true;
        json["schemaVersion"] = 99;
        json["referenceAssemblyAliases"] = JsonNode.Parse("""{"System.Private.CoreLib":"NetWasm.CoreLib"}""");

        var manifest = Parser().Parse(json.ToJsonString());

        Assert.Equal(99, manifest.SchemaVersion);
        Assert.Equal("ExactOracle", manifest.DesktopEntryMethod);
        Assert.Equal(CorpusExecutionBackend.Linked, manifest.ExecutionBackend);
        Assert.Equal("NetWasm.CoreLib", manifest.ReferenceAssemblyAliases["System.Private.CoreLib"]);
        Assert.Equal("  Exact declared reason.  ", manifest.SameSourceReason);
        Assert.Equal((OracleRuntimeCapabilities)7, manifest.RequiredRuntimeCapabilities);
        Assert.True(manifest.AllowUnsafe);
        Assert.True(manifest.RequiresReactor);
        Assert.True(manifest.SupportsBatchedOracle);
        Assert.True(manifest.ReportAllMismatches);
        Assert.False(manifest.ExposesLegacyTrace);
        Assert.True(manifest.UsesTypedTrace);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \r\n")]
    public void RejectsAbsentText(string? json) =>
        Assert.ThrowsAny<ArgumentException>(() => Parser().Parse(json!));

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("{}")]
    [InlineData("{invalid")]
    public void RejectsAbsentOrMalformedObject(string json) =>
        Assert.Throws<JsonException>(() => Parser().Parse(json));

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("caseId")]
    [InlineData("featureIds")]
    [InlineData("inputKind")]
    [InlineData("sourceFiles")]
    [InlineData("name")]
    [InlineData("namespace")]
    [InlineData("entryMethod")]
    [InlineData("inputs")]
    [InlineData("expectations")]
    [InlineData("referencePaths")]
    [InlineData("oracleMode")]
    [InlineData("matrixProfile")]
    [InlineData("testMethod")]
    public void RejectsEachMissingRequiredField(string field)
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        Assert.True(json.Remove(field));
        Assert.Throws<JsonException>(() => Parser().Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData("caseId")]
    [InlineData("name")]
    [InlineData("namespace")]
    [InlineData("entryMethod")]
    [InlineData("testMethod")]
    public void RejectsNullNonNullableText(string field)
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        json[field] = null;
        Assert.Throws<JsonException>(() => Parser().Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData("inputKind", "0")]
    [InlineData("inputKind", "\"0\"")]
    [InlineData("inputKind", "\"TextualIl\"")]
    [InlineData("oracleMode", "1")]
    [InlineData("oracleMode", "\"Unknown\"")]
    [InlineData("matrixProfile", "2")]
    [InlineData("matrixProfile", "\"Unknown\"")]
    [InlineData("executionBackend", "1")]
    [InlineData("executionBackend", "\"1\"")]
    [InlineData("executionBackend", "\"Unknown\"")]
    [InlineData("requiredRuntimeCapabilities", "7")]
    [InlineData("requiredRuntimeCapabilities", "\"8\"")]
    [InlineData("requiredRuntimeCapabilities", "\"Unknown\"")]
    [InlineData("inputs", "[2147483648]")]
    [InlineData("inputs", "[-2147483649]")]
    [InlineData("inputs", "[9007199254740993]")]
    [InlineData("inputs", "[1.0]")]
    [InlineData("inputs", "[1e0]")]
    [InlineData("inputs", "[\"1\"]")]
    [InlineData("inputs", "[\"NaN\"]")]
    [InlineData("inputs", "[null]")]
    [InlineData("inputs", "null")]
    [InlineData("requiresReactor", "null")]
    [InlineData("requiresReactor", "1")]
    [InlineData("requiresReactor", "\"true\"")]
    [InlineData("featureIds", "null")]
    [InlineData("expectations", "null")]
    [InlineData("referenceAssemblyAliases", "null")]
    [InlineData("expectations", "[{\"input\":0,\"returnValue\":2147483648,\"exceptionType\":null}]")]
    [InlineData("expectations", "[{\"input\":0,\"returnValue\":\"42\",\"exceptionType\":null}]")]
    [InlineData("expectations", "[{\"input\":0,\"returnValue\":42}]")]
    [InlineData("expectations", "[{\"input\":0,\"returnValue\":42,\"exceptionType\":null,\"unknown\":true}]")]
    public void RejectsWrongTypesCoercionMissingNestedFieldsAndUnknownEnums(string field, string value)
    {
        var json = JsonNode.Parse(Valid)!.AsObject();
        json[field] = JsonNode.Parse(value);
        Assert.Throws<JsonException>(() => Parser().Parse(json.ToJsonString()));
    }

    [Theory]
    [InlineData("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"schemaVersion\": 1,")]
    [InlineData("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"schemaVersion\": 2,")]
    [InlineData("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"SchemaVersion\": 1,")]
    [InlineData("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"unknown\": null,")]
    [InlineData("\"schemaVersion\": 1,", "\"schemaVersion\": 1, // comment\n")]
    [InlineData("\"input\": -2147483648,", "\"input\": -2147483648, \"input\": -2147483648,")]
    [InlineData("\"returnValue\": 2147483647,", "\"returnValue\": 2147483647, \"returnValue\": 0,")]
    [InlineData("\"exceptionType\": null }", "\"exceptionType\": null, }")]
    [InlineData("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"referenceAssemblyAliases\": {\"A\":\"B\",\"A\":\"C\"},")]
    public void RejectsAmbiguousUnknownAndNonJsonDeclarations(string before, string after)
    {
        Assert.Contains(before, Valid, StringComparison.Ordinal);
        Assert.Throws<JsonException>(() => Parser().Parse(Valid.Replace(before, after, StringComparison.Ordinal)));
    }

    private static ICorpusCaseManifestParser Parser() =>
        Assert.IsAssignableFrom<ICorpusCaseManifestParser>(new CorpusCaseManifestParser());
}
