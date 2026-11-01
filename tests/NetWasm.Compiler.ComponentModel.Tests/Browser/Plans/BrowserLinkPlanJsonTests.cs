using System.Text.Json;
using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Browser.Plans;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkPlanJsonTests
{
    private const string Contract = """
        {
          "schemaVersion": 1,
          "requiredCapabilities": ["text", "tool", "inline", "exports", "validate", "move"],
          "inputs": ["application.wasm", "runtime.wasm"],
          "outputs": ["result.wasm"],
          "temporaryPaths": ["support.wasm", "selected.wasm", "merged.wasm"],
          "operations": [
            {"kind":"text","outputPath":"support.wasm","text":"support module"},
            {"kind":"inline","inputPath":"application.wasm","outputPath":"selected.wasm","selection":"SizeAware"},
            {"kind":"tool","toolId":"wasm-merge","arguments":["selected.wasm","application","--output","merged.wasm"],"inputPaths":["selected.wasm"],"outputPaths":["merged.wasm"]},
            {"kind":"exports","inputPath":"merged.wasm","outputPath":"merged.wasm","componentPrefix":null,"removedExports":[{"name":"internal","kind":0}]},
            {"kind":"validate","path":"merged.wasm","arguments":["validate","merged.wasm"]},
            {"kind":"move","inputPath":"merged.wasm","outputPath":"result.wasm"}
          ]
        }
        """;

    [Fact]
    public void SerializesEveryFieldOfEveryOperationWithoutProjection()
    {
        var serializer = CreateSerializer();
        var plan = CreatePlan();

        using var actual = JsonDocument.Parse(serializer.Serialize(plan));
        using var expected = JsonDocument.Parse(Contract);

        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
    }

    [Fact]
    public void DeserializesTheCompleteTypedContractAndPreservesOperationOrder()
    {
        var expected = CreatePlan();

        var actual = BrowserLinkPlanDeserializer.Deserialize(Contract);

        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.RequiredCapabilities, actual.RequiredCapabilities);
        Assert.Equal(expected.Inputs, actual.Inputs);
        Assert.Equal(expected.Outputs, actual.Outputs);
        Assert.Equal(expected.TemporaryPaths, actual.TemporaryPaths);
        Assert.Collection(actual.Operations,
            value => Assert.Equal(expected.Operations[0], Assert.IsType<BrowserLinkTextOperation>(value)),
            value => Assert.Equal(expected.Operations[1], Assert.IsType<BrowserLinkInlineOperation>(value)),
            value =>
            {
                var tool = Assert.IsType<BrowserLinkToolOperation>(value);
                var expectedTool = Assert.IsType<BrowserLinkToolOperation>(expected.Operations[2]);
                Assert.Equal(expectedTool.ToolId, tool.ToolId);
                Assert.Equal(expectedTool.Arguments, tool.Arguments);
                Assert.Equal(expectedTool.InputPaths, tool.InputPaths);
                Assert.Equal(expectedTool.OutputPaths, tool.OutputPaths);
            },
            value =>
            {
                var exports = Assert.IsType<BrowserLinkExportOperation>(value);
                Assert.Equal("merged.wasm", exports.InputPath);
                Assert.Equal("merged.wasm", exports.OutputPath);
                Assert.Null(exports.ComponentPrefix);
                Assert.Equal(new WasmInternalExport("internal", 0), Assert.Single(exports.RemovedExports));
            },
            value =>
            {
                var validation = Assert.IsType<BrowserLinkValidationOperation>(value);
                Assert.Equal("merged.wasm", validation.Path);
                Assert.Collection(validation.Arguments,
                    argument => Assert.Equal("validate", argument),
                    argument => Assert.Equal("merged.wasm", argument));
            },
            value => Assert.Equal(expected.Operations[5], Assert.IsType<BrowserLinkMoveOperation>(value)));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("not json")]
    public void RejectsMissingOrMalformedTransportObjects(string json)
    {
        Assert.Throws<JsonException>(() => BrowserLinkPlanDeserializer.Deserialize(json));
    }

    [Fact]
    public void RejectsUnknownOperationKindsAndUnrecognizedFields()
    {
        Assert.Throws<JsonException>(() => BrowserLinkPlanDeserializer.Deserialize(Contract.Replace("\"text\"", "\"unknown\"")));
        Assert.Throws<JsonException>(() => BrowserLinkPlanDeserializer.Deserialize(Contract.Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, \"unknown\": true,")));
        Assert.Throws<JsonException>(() => BrowserLinkPlanDeserializer.Deserialize(Contract.Replace("\"text\":\"support module\"", "\"unknown\":\"support module\"")));
    }

    [Fact]
    public void RejectsNullOrBlankArguments()
    {
        var serializer = CreateSerializer();
        Assert.Throws<ArgumentNullException>(() => serializer.Serialize((BrowserLinkPlan)null!));
        Assert.Throws<ArgumentNullException>(() => serializer.Serialize((BrowserLinkExecutionPlan)null!));
        Assert.Throws<ArgumentNullException>(() => BrowserLinkPlanDeserializer.Deserialize(null!));
        Assert.Throws<ArgumentException>(() => BrowserLinkPlanDeserializer.Deserialize(""));
        Assert.Throws<ArgumentException>(() => BrowserLinkPlanDeserializer.Deserialize(" "));
    }

    private static IBrowserLinkPlanSerializer CreateSerializer() =>
        Assert.IsAssignableFrom<IBrowserLinkPlanSerializer>(new BrowserLinkPlanSerializer());


    private static BrowserLinkPlan CreatePlan() => new(1,
        ["text", "tool", "inline", "exports", "validate", "move"],
        ["application.wasm", "runtime.wasm"], ["result.wasm"],
        ["support.wasm", "selected.wasm", "merged.wasm"],
        [
            new BrowserLinkTextOperation("support.wasm", "support module"),
            new BrowserLinkInlineOperation("application.wasm", "selected.wasm", WasmInlineAnnotationSelection.SizeAware),
            new BrowserLinkToolOperation("wasm-merge", ["selected.wasm", "application", "--output", "merged.wasm"],
                ["selected.wasm"], ["merged.wasm"]),
            new BrowserLinkExportOperation("merged.wasm", "merged.wasm", null, [new("internal", 0)]),
            new BrowserLinkValidationOperation("merged.wasm", ["validate", "merged.wasm"]),
            new BrowserLinkMoveOperation("merged.wasm", "result.wasm"),
        ]);
}
