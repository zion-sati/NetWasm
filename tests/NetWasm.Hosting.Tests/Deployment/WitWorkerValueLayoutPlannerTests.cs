using System.Text.Json.Nodes;
using NetWasm.Hosting.Deployment;

namespace NetWasm.Hosting.Tests.Deployment;

public sealed class WitWorkerValueLayoutPlannerTests
{
    [Theory]
    [InlineData("URL-value", "urlValue")]
    [InlineData("HTTP-status", "httpStatus")]
    [InlineData("foo-BAR", "fooBar")]
    [InlineData("URL", "url")]
    [InlineData("url", "url")]
    [InlineData("HTTP2-3D-value", "http23dValue")]
    public void MemberNamesMatchJcoForUppercaseWitSegments(string name, string expected)
    {
        Assert.Equal(expected, WitWorkerValueLayoutPlanner.MemberName(name));
    }

    [Fact]
    public void NullableOptionsAlternateAndNumericAliasesKeepTypedArrays()
    {
        var fixture = WitWorkerContractReaderTests.Fixture();
        fixture["types"] = JsonNode.Parse("[{\"id\":42,\"kind\":{\"option\":43}},{\"id\":43,\"kind\":{\"option\":44}},{\"id\":44,\"kind\":{\"option\":45}},{\"id\":45,\"kind\":{\"type\":\"s8\"}}]");
        var result = new WitWorkerValueLayoutPlanner().Plan(WitWorkerContractReaderTests.Read(fixture));
        Assert.True(result[42].Nullable); Assert.False(result[43].Nullable); Assert.True(result[44].Nullable);
        Assert.Equal("Int8Array", result[45].NumericArray);
        Assert.Null(result[42].NumericArray);
        Assert.Equal("Int8Array", WitWorkerValueLayoutPlanner.NumericArray(new("defined", null, 45), result));
        Assert.Equal("Float64Array", WitWorkerValueLayoutPlanner.NumericArray(new("primitive", "f64", null), result));
        Assert.Null(WitWorkerValueLayoutPlanner.NumericArray(new("primitive", "string", null), result));
    }

    [Fact]
    public void EqualNamesInDifferentOwnersNeverCollapse()
    {
        var fixture = WitWorkerContractReaderTests.Fixture();
        var second = fixture["types"]![0]!.DeepClone(); second["id"] = 43;
        second["owner"]!["identity"] = "example:other/values@1.0.0";
        fixture["types"]!.AsArray().Add(second);
        var planner = new WitWorkerValueLayoutPlanner();
        var first = planner.Plan(WitWorkerContractReaderTests.Read(fixture));
        var again = planner.Plan(WitWorkerContractReaderTests.Read(fixture));
        Assert.NotEqual(first[42].Name, first[43].Name);
        Assert.Equal(first[42].Name, again[42].Name);
        Assert.False(first[42].Nullable);
        Assert.Equal("roundTrip", WitWorkerValueLayoutPlanner.MemberName("round-trip"));
        Assert.Throws<ArgumentNullException>(() => planner.Plan(null!));
    }

    [Fact]
    public void AliasesFollowNumericRepresentationAndReferencesKeepTheirKinds()
    {
        var fixture = WitWorkerContractReaderTests.Fixture();
        fixture["types"] = JsonNode.Parse("[{\"id\":42,\"kind\":{\"type\":43}},{\"id\":43,\"kind\":{\"type\":\"u64\"}}]");
        var layouts = new WitWorkerValueLayoutPlanner().Plan(WitWorkerContractReaderTests.Read(fixture));
        Assert.Equal("BigUint64Array", layouts[42].NumericArray); Assert.False(layouts[42].Nullable);
        using var primitive = System.Text.Json.JsonDocument.Parse("\"bool\"");
        using var defined = System.Text.Json.JsonDocument.Parse("42");
        Assert.Equal(new("primitive", "bool", null), WitWorkerValueLayoutPlanner.ReadReference(primitive.RootElement));
        Assert.Equal(new("defined", null, 42), WitWorkerValueLayoutPlanner.ReadReference(defined.RootElement));
    }
}
