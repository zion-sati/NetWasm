using System.Text.Json;
using NetWasm.Compiler.Core;

using NetWasm.Wit.Bindings.TypeDefinitions;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitBindingSyntaxFormatterTests
{
    [Fact]
    public void FormatsNamesAndDeclarationsThroughOneContract()
    {
        var document = new WitDocument(
            [],
            [],
            [],
            [new WitTypeDefinition(
                0,
                "point",
                Json("{\"record\":{\"fields\":[]}}"),
                null)],
            "{}");
        var function = new WitFunction(
            "[constructor]open-file",
            [new WitParameter("in", new WitTypeReference.Primitive("u32"))],
            new WitTypeReference.Defined(0),
            new WitFunctionKind("freestanding"));

        Assert.Equal(
            "NetWasm.Wit.Example.Test",
            Format(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
                new WitBindingSyntaxRequest.PackageNamespace("example:test")));
        Assert.Equal(
            "CreateOpenFile",
            Format(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
                new WitBindingSyntaxRequest.FunctionName(function.Name)));
        Assert.Equal(
            "Poll",
            Format(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
                new WitBindingSyntaxRequest.FunctionName("poll")));
        Assert.Equal(
            "Poll",
            Format(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
                new WitBindingSyntaxRequest.FunctionName("[method]poll")));
        Assert.Equal(
            "Read",
            Format(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
                new WitBindingSyntaxRequest.FunctionName("resource.read")));
        Assert.Equal(
            "_7Segment",
            Format(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
                new WitBindingSyntaxRequest.Identifier("7-segment")));
        Assert.Equal(
            "_",
            Format(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
                new WitBindingSyntaxRequest.Identifier("")));
        Assert.Equal(
            "uint @in",
            Format(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
                new WitBindingSyntaxRequest.Parameters(
                    document,
                    function)));
        Assert.Equal(
            "Point",
            Format(new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier()),
                new WitBindingSyntaxRequest.TypeName(
                    document,
                    new WitTypeReference.Defined(0))));
    }

    [Fact]
    public void FormatsAnonymousTypesAndRejectsUnknownReferences()
    {
        var document = new WitDocument(
            [],
            [],
            [],
            [
                Type(0, null, "{\"list\":\"u32\"}"),
                Type(1, null, "{\"option\":\"string\"}"),
                Type(2, null, "{\"tuple\":{\"types\":[\"s32\",\"f64\"]}}"),
                Type(3, null, "{\"result\":{\"ok\":\"u32\",\"err\":null}}"),
                Type(4, null, "{\"handle\":{\"own\":5}}"),
                Type(5, "resource", "\"resource\""),
                Type(6, null, "{\"type\":\"u32\"}"),
                Type(7, null, "{\"mystery\":\"u32\"}"),
                Type(8, null, "{\"list\":0}"),
                Type(9, null, "{\"option\":0}"),
            ],
            "{}");
        var formatter = new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier());

        foreach (var definition in document.Types)
        {
            if (definition.Kind.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var kind = definition.Kind.EnumerateObject().Single();
            if (kind.Name != "list")
            {
                continue;
            }

            var direct = formatter.Format(new WitBindingSyntaxRequest.TypeDefinitionName(
                document,
                definition));

            Assert.NotEmpty(direct);
            Assert.NotEqual(definition.Name, direct);
        }

        Assert.Equal("uint[]", formatter.Format(new WitBindingSyntaxRequest.TypeName(
            document, new WitTypeReference.Defined(0))));
        Assert.Equal("WitOption<string>", formatter.Format(new WitBindingSyntaxRequest.TypeName(
            document, new WitTypeReference.Defined(1))));
        Assert.Equal("(int, double)", formatter.Format(new WitBindingSyntaxRequest.TypeName(
            document, new WitTypeReference.Defined(2))));
        Assert.Equal("WitResult<uint, WitUnit>", formatter.Format(new WitBindingSyntaxRequest.TypeName(
            document, new WitTypeReference.Defined(3))));
        Assert.Equal("Resource", formatter.Format(new WitBindingSyntaxRequest.TypeName(
            document, new WitTypeReference.Defined(4))));
        Assert.Equal("uint", formatter.Format(new WitBindingSyntaxRequest.TypeName(
            document, new WitTypeReference.Defined(6))));
        Assert.Equal("new byte[(int)length]", formatter.Format(
            new WitBindingSyntaxRequest.ArrayCreation(
                document,
                new WitTypeReference.Primitive("u8"),
                "(int)length")));
        Assert.Equal("new uint[(int)length][]", formatter.Format(
            new WitBindingSyntaxRequest.ArrayCreation(
                document,
                new WitTypeReference.Defined(0),
                "(int)length")));
        Assert.Equal("new uint[(int)length][][]", formatter.Format(
            new WitBindingSyntaxRequest.ArrayCreation(
                document,
                new WitTypeReference.Defined(8),
                "(int)length")));
        Assert.Equal("new WitOption<uint[]>[(int)length]", formatter.Format(
            new WitBindingSyntaxRequest.ArrayCreation(
                document,
                new WitTypeReference.Defined(9),
                "(int)length")));
        Assert.Throws<ArgumentException>(() => formatter.Format(
            new WitBindingSyntaxRequest.ArrayCreation(
                document,
                new WitTypeReference.Primitive("u8"),
                " ")));
        Assert.Throws<WitBindingException>(() => formatter.Format(
            new WitBindingSyntaxRequest.TypeName(
                document, new WitTypeReference.Defined(7))));

        Assert.Throws<InvalidOperationException>(() => formatter.Format(
            new WitBindingSyntaxRequest.TypeName(document, new UnknownReference())));
        Assert.Throws<ArgumentOutOfRangeException>(() => formatter.Format(
            new UnknownRequest()));
        Assert.Throws<WitBindingException>(() => formatter.Format(
            new WitBindingSyntaxRequest.TypeName(
                document, new WitTypeReference.Primitive("i128"))));
    }

    [Fact]
    public void FormatsQualifiedFunctionNamesThroughOneContract()
    {
        IWitBindingSyntaxFormatter[] formatters = [new WitBindingSyntaxFormatter(new WitTypeDefinitionClassifier())];

        var result = formatters[0].Format(new WitBindingSyntaxRequest.QualifiedFunctionName(
            "http-types",
            "send-request"));

        Assert.Equal("HttpTypes_SendRequest", result);
    }

    private static WitTypeDefinition Type(int id, string? name, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new WitTypeDefinition(id, name, document.RootElement.Clone(), null);
    }

    private sealed record UnknownReference : WitTypeReference;

    private sealed record UnknownRequest : WitBindingSyntaxRequest;

    private static string Format(
        object formatter,
        WitBindingSyntaxRequest request) =>
        ((IWitBindingSyntaxFormatter)formatter).Format(request);

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }
}
