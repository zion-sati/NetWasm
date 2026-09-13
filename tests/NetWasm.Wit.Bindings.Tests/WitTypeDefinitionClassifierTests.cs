using System;
using System.Text.Json;
using NetWasm.Wit.Bindings.TypeDefinitions;
using Xunit;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitTypeDefinitionClassifierTests
{
    private readonly IWitTypeDefinitionClassifier _classifier =
        new IWitTypeDefinitionClassifier[] { new WitTypeDefinitionClassifier() }[0];

    [Fact]
    public void ClassifyRejectsNullDefinition()
    {
        Assert.Throws<ArgumentNullException>(() => _classifier.Classify(null!));
    }

    [Fact]
    public void ClassifyRejectsNonObjectDefinition()
    {
        Assert.Equal(WitTypeDefinitionCategory.Unsupported, _classifier.Classify(Definition("sample", "[]")));
    }

    [Fact]
    public void ClassifyRejectsEmptyDefinition()
    {
        Assert.Equal(WitTypeDefinitionCategory.Unsupported, _classifier.Classify(Definition("sample", "{}")));
    }

    [Fact]
    public void ClassifyRejectsDefinitionWithMultipleKinds()
    {
        Assert.Equal(
            WitTypeDefinitionCategory.Unsupported,
            _classifier.Classify(Definition("sample", "{\"record\":{},\"type\":{}}")));
    }

    [Theory]
    [InlineData("record")]
    [InlineData("variant")]
    [InlineData("enum")]
    [InlineData("flags")]
    [InlineData("resource")]
    public void ClassifyRecognizesNominalDefinitions(string kind)
    {
        Assert.Equal(
            WitTypeDefinitionCategory.Nominal,
            _classifier.Classify(Definition("sample", $"{{\"{kind}\":{{}}}}")));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("list")]
    [InlineData("option")]
    [InlineData("tuple")]
    [InlineData("result")]
    public void ClassifyRecognizesTransparentAliases(string kind)
    {
        Assert.Equal(
            WitTypeDefinitionCategory.TransparentAlias,
            _classifier.Classify(Definition("sample", $"{{\"{kind}\":{{}}}}")));
    }

    [Fact]
    public void ClassifyRejectsUnknownDefinitionKind()
    {
        Assert.Equal(
            WitTypeDefinitionCategory.Unsupported,
            _classifier.Classify(Definition("sample", "{\"future\":{}}")));
    }

    private static WitTypeDefinition Definition(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        var definition = document.RootElement.Clone();
        return new WitTypeDefinition(0, name, definition, 0);
    }
}
