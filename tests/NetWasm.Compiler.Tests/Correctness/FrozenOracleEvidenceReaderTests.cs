using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class FrozenOracleEvidenceReaderTests : IDisposable
{
    private static readonly ImmutableArray<int> Inputs = [0, 1, 2, 3, 4, 5, 6, 7, 8];
    private readonly string evidencePath = WriteEvidence(CreateEvidence());

    public void Dispose()
    {
        DeleteEvidence(evidencePath);
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void ReadsAllFrozenObservationsThroughItsCapabilityInterface()
    {
        var reader = CreateReader();
        var observations = reader.Read(CreateRequest());

        Assert.Equal(9, observations.Count);
        Assert.Equal(101, observations[0].Value);
        Assert.Equal(109, observations[8].Value);
    }

    [Fact]
    public void RejectsNullAndBlankRequests()
    {
        var reader = CreateReader();
        Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
        Assert.Throws<ArgumentException>(() => reader.Read(
            CreateRequest() with { RelativePath = string.Empty }));
        Assert.Throws<ArgumentException>(() => reader.Read(
            CreateRequest() with { FixtureName = " " }));
    }

    [Fact]
    public void RejectsRuntimeSourceAndAssemblyIdentityChanges()
    {
        var reader = CreateReader();
        Assert.Throws<InvalidOperationException>(() => reader.Read(
            CreateRequest() with { RuntimeVersion = "different" }));
        Assert.Throws<InvalidOperationException>(() => reader.Read(
            CreateRequest() with { SourceSha256 = "different" }));
        Assert.Throws<InvalidOperationException>(() => reader.Read(
            CreateRequest() with { DesktopAssemblySha256 = "different" }));
    }

    [Fact]
    public void RejectsMissingProfileAndObservationCount()
    {
        var reader = CreateReader();
        Assert.Throws<InvalidOperationException>(() => reader.Read(
            CreateRequest() with { Profile = (CilProfile)99 }));
        Assert.Throws<InvalidOperationException>(() => reader.Read(
            CreateRequest() with { Inputs = [0] }));
    }

    [Fact]
    public void RejectsProfileMissingFromEvidence()
    {
        var evidence = LoadEvidence();
        evidence["profiles"]!.AsObject().Remove("Debug");
        var path = WriteEvidence(evidence);
        try
        {
            var reader = CreateReader();
            Assert.Throws<InvalidOperationException>(() => reader.Read(
                CreateRequest() with { RelativePath = path }));
        }
        finally
        {
            DeleteEvidence(path);
        }
    }

    [Fact]
    public void RejectsMissingHashesAfterIdentityValidation()
    {
        var evidence = LoadEvidence();
        evidence.Remove("hashes");
        var path = WriteEvidence(evidence);
        try
        {
            var reader = CreateReader();
            Assert.Throws<InvalidOperationException>(() => reader.Read(
                CreateRequest() with { RelativePath = path }));
        }
        finally
        {
            DeleteEvidence(path);
        }
    }

    [Fact]
    public void RejectsMissingCanonicalHashMapOrProfile()
    {
        var withoutMap = LoadEvidence();
        withoutMap["hashes"]!.AsObject().Remove("canonicalOracleSha256");
        var withoutMapPath = WriteEvidence(withoutMap);
        try
        {
            var reader = CreateReader();
            Assert.Throws<InvalidOperationException>(() => reader.Read(
                CreateRequest() with { RelativePath = withoutMapPath }));
        }
        finally
        {
            DeleteEvidence(withoutMapPath);
        }

        var withoutProfile = LoadEvidence();
        withoutProfile["hashes"]!["canonicalOracleSha256"]!.AsObject().Remove("Debug");
        var withoutProfilePath = WriteEvidence(withoutProfile);
        try
        {
            var reader = CreateReader();
            Assert.Throws<InvalidOperationException>(() => reader.Read(
                CreateRequest() with { RelativePath = withoutProfilePath }));
        }
        finally
        {
            DeleteEvidence(withoutProfilePath);
        }
    }

    [Fact]
    public void ReadsCanonicalNullValueAndExceptionFields()
    {
        var evidence = LoadEvidence();
        var observation = evidence["profiles"]!["Debug"]!["0"]!.AsObject();
        observation["value"] = null;
        observation["exceptionType"] = "System.UriFormatException";
        evidence["hashes"]!["canonicalOracleSha256"]!["Debug"] =
            CanonicalHash(evidence["profiles"]!["Debug"]!.AsObject());
        var path = WriteEvidence(evidence);
        try
        {
            var reader = CreateReader();
            var observations = reader.Read(CreateRequest() with { RelativePath = path });
            Assert.Null(observations[0].Value);
            Assert.Equal("System.UriFormatException", observations[0].ExceptionType);
        }
        finally
        {
            DeleteEvidence(path);
        }
    }

    [Fact]
    public void RejectsMissingInputAfterCanonicalValidation()
    {
        var reader = CreateReader();
        Assert.Throws<InvalidOperationException>(() => reader.Read(
            CreateRequest() with { Inputs = [0, 1, 2, 3, 4, 5, 6, 7, 9] }));
    }

    [Fact]
    public void RejectsCanonicalHashChanges()
    {
        var evidence = LoadEvidence();
        evidence["hashes"]!["canonicalOracleSha256"]!["Debug"] = "different";
        var path = WriteEvidence(evidence);
        try
        {
            var reader = CreateReader();
            Assert.Throws<InvalidOperationException>(() => reader.Read(
                CreateRequest() with { RelativePath = path }));
        }
        finally
        {
            DeleteEvidence(path);
        }
    }

    [Fact]
    public void RejectsInvalidSchemaAndEmptyEvidence()
    {
        var invalidSchema = LoadEvidence();
        invalidSchema["schemaVersion"] = 1;
        var invalidPath = WriteEvidence(invalidSchema);
        try
        {
            var reader = CreateReader();
            Assert.Throws<InvalidOperationException>(() => reader.Read(
                CreateRequest() with { RelativePath = invalidPath }));
        }
        finally
        {
            DeleteEvidence(invalidPath);
        }

        var emptyPath = WriteEvidenceText(string.Empty);
        try
        {
            var reader = CreateReader();
            Assert.Throws<JsonException>(() => reader.Read(
                CreateRequest() with { RelativePath = emptyPath }));
        }
        finally
        {
            DeleteEvidence(emptyPath);
        }

        var nullPath = WriteEvidenceText("null");
        try
        {
            var reader = CreateReader();
            Assert.Throws<InvalidOperationException>(() => reader.Read(
                CreateRequest() with { RelativePath = nullPath }));
        }
        finally
        {
            DeleteEvidence(nullPath);
        }
    }

    private FrozenOracleRequest CreateRequest() => new(
        evidencePath,
        "SyntheticFrozenOracle",
        CilProfile.Debug,
        Inputs,
        "65e797676c1355bbc0d3ab68ccb8f0dc7e02e4a7db7f90f80f531c4c12719768",
        "3f1f81c3ebc8a964cc93cbffaadc59cbacf078c28cb09a2bdd870da6568c2ffd",
        "10.0.302",
        ".NETCoreApp,Version=v8.0",
        "10.0.10",
        ".NET 10.0.10",
        "Arm64");

#pragma warning disable CA1859 // The test intentionally exercises the one-action interface contract.
    private static IFrozenOracleEvidenceReader CreateReader() =>
        new FrozenOracleEvidenceReader();
#pragma warning restore CA1859

    private JsonObject LoadEvidence() => JsonNode.Parse(File.ReadAllText(evidencePath))!.AsObject();

    // Parser contract data, not historical runtime qualification evidence.
    private static JsonObject CreateEvidence()
    {
        var observations = new JsonObject();
        foreach (var input in Inputs)
        {
            observations[input.ToString(CultureInfo.InvariantCulture)] = new JsonObject
            {
                ["kind"] = "Value", ["value"] = 101 + input,
                ["exceptionType"] = null, ["trace"] = 0,
            };
        }
        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["fixture"] = "SyntheticFrozenOracle",
            ["runtimeIdentity"] = new JsonObject
            {
                ["sdkVersion"] = "10.0.302", ["targetFramework"] = ".NETCoreApp,Version=v8.0",
                ["runtimeVersion"] = "10.0.10", ["frameworkDescription"] = ".NET 10.0.10",
                ["processArchitecture"] = "Arm64",
            },
            ["hashes"] = new JsonObject
            {
                ["sourceSha256"] = "65e797676c1355bbc0d3ab68ccb8f0dc7e02e4a7db7f90f80f531c4c12719768",
                ["desktopAssemblySha256"] = new JsonObject
                {
                    ["Debug"] = "3f1f81c3ebc8a964cc93cbffaadc59cbacf078c28cb09a2bdd870da6568c2ffd",
                },
                ["canonicalOracleSha256"] = new JsonObject
                {
                    // Independently frozen hash; do not compute this with the reader's algorithm.
                    ["Debug"] = "f974504fe46a1fcc7c6e5824006c6c9771d6812aca93f0e9143e7e43004c5e6f",
                },
            },
            ["profiles"] = new JsonObject { ["Debug"] = observations },
        };
    }

    private static string WriteEvidence(JsonObject evidence) =>
        WriteEvidenceText(evidence.ToJsonString());

    private static string CanonicalHash(JsonObject observations)
    {
        var canonical = string.Join(
            "\n",
            observations
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => string.Join(
                    "|",
                    pair.Key,
                    pair.Value!["kind"]!.GetValue<string>(),
                    pair.Value["value"] is JsonValue valueNode
                        ? valueNode.GetValue<int>().ToString(CultureInfo.InvariantCulture)
                        : string.Empty,
                    pair.Value["exceptionType"] is JsonValue exceptionNode
                        ? exceptionNode.GetValue<string>()
                        : string.Empty,
                    pair.Value["trace"]!.GetValue<int>().ToString(CultureInfo.InvariantCulture))));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))
            .ToLowerInvariant();
    }

    private static string WriteEvidenceText(string text)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, text);
        return path;
    }

    private static void DeleteEvidence(string path) => File.Delete(path);
}
