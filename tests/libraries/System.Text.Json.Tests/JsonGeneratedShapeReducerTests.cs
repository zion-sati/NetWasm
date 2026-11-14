using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.Text.Json.Tests;

[JsonSerializable(typeof(GeneratedMixedModel))]
internal sealed partial class GeneratedMixedContext : JsonSerializerContext;

internal sealed record GeneratedMixedChild(string Value);

internal sealed record GeneratedMixedModel(
    string Text,
    int? Number,
    GeneratedMixedChild First,
    global::System.Collections.Generic.List<string> Items,
    global::System.Collections.Generic.Dictionary<string, int> Map,
    GeneratedMixedChild Second);

public sealed class JsonGeneratedShapeReducerTests
{
    [Test]
    public async Task GeneratedMixedShapeRoundTrips()
    {
        var expected = new GeneratedMixedModel(
            "text",
            7,
            new GeneratedMixedChild("first"),
            new global::System.Collections.Generic.List<string> { "item" },
            new global::System.Collections.Generic.Dictionary<string, int> { ["key"] = 42 },
            new GeneratedMixedChild("second"));

        var json = JsonSerializer.Serialize(
            expected,
            GeneratedMixedContext.Default.GeneratedMixedModel);
        var actual = JsonSerializer.Deserialize(
            json,
            GeneratedMixedContext.Default.GeneratedMixedModel)
            ?? throw new InvalidOperationException(
                "Generated mixed shape deserialized to null.");

        await Assert.That(actual.Text).IsEqualTo(expected.Text);
        await Assert.That(actual.Number).IsEqualTo(expected.Number);
        await Assert.That(actual.First.Value).IsEqualTo(expected.First.Value);
        await Assert.That(actual.Second.Value).IsEqualTo(expected.Second.Value);
        await Assert.That(actual.Items.Count).IsEqualTo(1);
        await Assert.That(actual.Items[0]).IsEqualTo("item");
        await Assert.That(actual.Map.Count).IsEqualTo(1);
        await Assert.That(actual.Map["key"]).IsEqualTo(42);
    }
}
