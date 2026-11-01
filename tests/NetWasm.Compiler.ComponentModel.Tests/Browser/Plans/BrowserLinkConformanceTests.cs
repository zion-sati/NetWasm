using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Browser.Plans;

namespace NetWasm.Compiler.ComponentModel.Tests.Browser.Plans;

public sealed class BrowserLinkConformanceTests
{
    [Fact]
    public void PortableFixtureMatchesCompilerValidatedAndMaterializedContract()
    {
        using var stream = typeof(BrowserLinkConformanceTests).Assembly.GetManifestResourceStream("ordered-link-v1.json");
        Assert.NotNull(stream);
        using var expected = JsonDocument.Parse(stream);
        var planJson = expected.RootElement.GetProperty("plan").GetRawText();
        var plan = BrowserLinkPlanDeserializer.Deserialize(planJson);
        var execution = BrowserLinkPlanValidation.CreateExecutionBuilder().Build(plan);
        using var actual = JsonDocument.Parse(new BrowserLinkPlanSerializer().Serialize(execution));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.Equal(["text", "inline", "tool", "exports", "validate", "move"], plan.RequiredCapabilities);
        Assert.Equal(6, plan.Operations.Length);
        Assert.Equal(2, execution.Invocations.Count);
    }
}
