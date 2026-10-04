using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Catalogs;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Catalogs;

public sealed class WitWorkerTypeReferenceReaderTests
{
    [Theory]
    [InlineData("{\"type\":3}", "3")]
    [InlineData("{\"list\":\"u8\"}", "u8")]
    [InlineData("{\"option\":4}", "4")]
    [InlineData("{\"record\":{\"fields\":[{\"name\":\"id\",\"type\":5},{\"name\":\"text\",\"type\":\"string\"}]}}", "5,string")]
    [InlineData("{\"tuple\":{\"types\":[6,\"s64\"]}}", "6,s64")]
    [InlineData("{\"variant\":{\"cases\":[{\"name\":\"empty\",\"type\":null},{\"name\":\"value\",\"type\":7}]}}", "7")]
    [InlineData("{\"result\":{\"ok\":null,\"err\":8}}", "8")]
    [InlineData("{\"result\":{\"ok\":9,\"err\":null}}", "9")]
    [InlineData("{\"handle\":{\"own\":10}}", "10")]
    [InlineData("{\"handle\":{\"borrow\":11}}", "11")]
    [InlineData("{\"enum\":{\"cases\":[{\"name\":\"one\"}]}}", "")]
    [InlineData("{\"flags\":{\"flags\":[]}}", "")]
    [InlineData("\"resource\"", "")]
    public void ReadsEachSupportedReferenceLayoutInOrder(string json, string expected)
    {
        using var parsed = JsonDocument.Parse(json);
        var references = Create().Read(parsed.RootElement);

        Assert.Equal(expected, string.Join(',', references.Select(reference => reference switch
        {
            WitTypeReference.Defined defined => defined.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            WitTypeReference.Primitive primitive => primitive.Name,
            _ => throw new InvalidOperationException(),
        })));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"list\":1,\"option\":1}")]
    [InlineData("\"unknown\"")]
    [InlineData("{\"future\":\"u8\"}")]
    [InlineData("{\"type\":null}")]
    [InlineData("{\"type\":1.5}")]
    [InlineData("{\"type\":true}")]
    [InlineData("{\"record\":{}}")]
    [InlineData("{\"record\":{\"fields\":true}}")]
    [InlineData("{\"record\":{\"fields\":[{}]}}")]
    [InlineData("{\"record\":{\"fields\":[{\"type\":null}]}}")]
    [InlineData("{\"record\":{\"fields\":[null]}}")]
    [InlineData("{\"record\":{\"fields\":[{\"name\":null,\"type\":\"u8\"}]}}")]
    [InlineData("{\"record\":{\"fields\":[{\"name\":\" \",\"type\":\"u8\"}]}}")]
    [InlineData("{\"record\":{\"fields\":[{\"name\":\"x\",\"type\":\"u8\"},{\"name\":\"x\",\"type\":\"u8\"}]}}")]
    [InlineData("{\"enum\":{\"cases\":[]}}")]
    [InlineData("{\"enum\":{\"cases\":[{\"wrong\":\"x\"}]}}")]
    [InlineData("{\"flags\":{\"flags\":[{\"name\":\"x\",\"extra\":true}]}}")]
    [InlineData("{\"variant\":{\"cases\":[]}}")]
    [InlineData("{\"tuple\":null}")]
    [InlineData("{\"tuple\":{\"types\":false}}")]
    [InlineData("{\"tuple\":{\"wrong\":[]}}")]
    [InlineData("{\"tuple\":{\"types\":[],\"extra\":0}}")]
    [InlineData("{\"result\":null}")]
    [InlineData("{\"result\":{}}")]
    [InlineData("{\"result\":{\"ok\":null,\"wrong\":null}}")]
    [InlineData("{\"result\":{\"wrong\":null,\"err\":null}}")]
    [InlineData("{\"handle\":null}")]
    [InlineData("{\"handle\":{}}")]
    [InlineData("{\"handle\":{\"share\":0}}")]
    [InlineData("{\"handle\":{\"own\":\"u8\"}}")]
    [InlineData("{\"handle\":{\"own\":1.5}}")]
    [InlineData("{\"resource\":\"resource\"}")]
    public void RejectsInvalidKindAndReferenceShapes(string json)
    {
        using var parsed = JsonDocument.Parse(json);

        Assert.Throws<CompilerException>(() => Create().Read(parsed.RootElement));
    }

    [Fact]
    public void RequiresExplicitRegistryAndPaths()
    {
        Assert.Throws<ArgumentNullException>(() => new WitWorkerTypeReferenceReader(null!));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerNamedCollectionReferenceReader("fields", true, false, 0, null, null!));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerTupleReferenceReader(null!));
        Assert.Throws<ArgumentNullException>(() => new WitWorkerResultReferenceReader(null!));
    }

    [Fact]
    public void EnforcesFlagLimitWithoutRejectingTheBoundary()
    {
        var members = Enumerable.Range(0, 33).Select(i => new { name = "flag" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) }).ToArray();
        using var valid = JsonDocument.Parse(JsonSerializer.Serialize(new { flags = new { flags = members.Take(32) } }));
        using var invalid = JsonDocument.Parse(JsonSerializer.Serialize(new { flags = new { flags = members } }));

        Assert.Empty(Create().Read(valid.RootElement));
        Assert.Throws<CompilerException>(() => Create().Read(invalid.RootElement));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"other\"")]
    public void ResourceReaderRejectsInvalidValues(string json)
    {
        using var value = JsonDocument.Parse(json);
        var reader = new WitWorkerResourceReferenceReader();
        Assert.Throws<CompilerException>(() => reader.Read(value.RootElement));
    }

    private static WitWorkerTypeReferenceReader Create() =>
        new WitWorkerTypeReferenceReader(WitWorkerReferenceComposition.CreateReaders());
}
