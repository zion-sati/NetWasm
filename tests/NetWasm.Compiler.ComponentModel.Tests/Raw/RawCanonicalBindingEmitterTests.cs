using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Raw;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

// Binding plans the adapter writer does not produce for valid WIT, emitted
// directly; RawAdapterExecutionTests runs the code it emits for real imports.
public sealed class RawCanonicalBindingEmitterTests
{
    private const string Freestanding =
        """{"interface":"sample:raw/api@1.0.0","javascriptName":"call","functionKind":"freestanding"}""";

    [Fact]
    public void RejectsFlatParametersThatDisagreeWithTheirTypes()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Emit(Freestanding, ["""{"kind":"u32"}"""], "null", ["i64"]));

        Assert.Contains("flat parameters disagree", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsFlatParametersBeyondItsParameters()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Emit(Freestanding, [], "null", ["i32"]));

        Assert.Contains("flat parameters disagree", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsADirectResultOfMoreThanOneCoreValue()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Emit(Freestanding, [], """{"kind":"text"}""", []));

        Assert.Contains("more than one core value", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReturnsNothingForAResultWithoutCoreValues()
    {
        var source = Emit(Freestanding, [], """{"kind":"unit"}""", []);

        Assert.Contains("const plan = rcUnit(value);", source, StringComparison.Ordinal);
        Assert.Contains("return undefined;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CallsAStaticResourceFunctionThroughItsClass()
    {
        var source = Emit(Provider("static", "open", "\"FileHandle\""), [], "null", []);

        Assert.Contains(
            "rcStatic(host.providers, \"sample:raw/api@1.0.0\", \"FileHandle\", \"open\")",
            source,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("method", "size", "null", "has no JavaScript class")]
    [InlineData("constructor", "Other", "\"FileHandle\"", "'constructor' is unsupported")]
    [InlineData("getter", "size", "\"FileHandle\"", "'getter' is unsupported")]
    public void RejectsResourceProvidersItCannotCall(string kind, string name, string resource, string message)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            Emit(Provider(kind, name, resource), [], "null", []));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TagsResultCasesThatCarryNoPayload()
    {
        const string status = """{"kind":"result","ok":null,"error":null}""";

        var source = Emit(Freestanding, [status], status, ["i32"]);

        Assert.Contains("return { tag: \"ok\" };", source, StringComparison.Ordinal);
        Assert.Contains("rcTaggedResult(\"ok\", false,", source, StringComparison.Ordinal);
        Assert.Contains("rcTagged(value, [\"ok\",\"err\"], [false, false], \"result\")", source, StringComparison.Ordinal);
        Assert.Contains("const lift = undefined;", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"kind":"result","ok":{"kind":"owned-resource","resourceType":0},"error":null}""")]
    [InlineData("""{"kind":"result","ok":null,"error":{"kind":"owned-resource","resourceType":0}}""")]
    [InlineData("""{"kind":"variant","cases":[{"name":"none","type":null},{"name":"file","type":{"kind":"owned-resource","resourceType":0}}]}""")]
    public void LiftsTheResourceACaseCarries(string selection)
    {
        var source = Emit(Freestanding, [selection], "null", ["i32", "i32"]);

        Assert.Contains("const lift = rcLiftTransaction(host, store);", source, StringComparison.Ordinal);
        Assert.Contains("lift.own(0, s0 >>> 0)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsAndWritesFourByteDiscriminants()
    {
        var cases = string.Join(",", Enumerable.Range(0, 65537).Select(index => $$"""{"name":"c{{index}}","type":null}"""));
        var wide = $$"""{"kind":"enum","cases":[{{cases}}]}""";

        var source = Emit(Freestanding, [wide], wide, [], indirectParameters: true, indirectResult: true);

        Assert.Contains(", rcView(memory).getUint32(index, true));", source, StringComparison.Ordinal);
        Assert.Contains("rcView(memory).setUint32(out, plan, true);", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("i32", "i64")]
    [InlineData("i64", "u8")]
    [InlineData("f64", "i64")]
    [InlineData("f32", "i32")]
    public void RejectsReadingAJoinedSlotAsACoreTypeItCannotHold(string joined, string selected)
    {
        Assert.Throws<InvalidOperationException>(() => RawCanonicalBindingEmitter.Coerce("slot", joined, selected));
    }

    private static string Provider(string kind, string name, string resource) => $$"""
        {"interface":"sample:raw/api@1.0.0","javascriptName":"{{name}}","functionKind":"{{kind}}","resourceJavaScriptName":{{resource}}}
        """;

    private static string Emit(
        string provider,
        string[] parameters,
        string result,
        string[] flatParameters,
        bool indirectParameters = false,
        bool indirectResult = false)
    {
        var arity = (indirectParameters ? 1 : flatParameters.Length) + (indirectResult ? 1 : 0);
        var binding = $$"""
            {
              "provider": {{provider}},
              "parameters": [{{string.Join(",", parameters.Select(parameter => $$"""{"type":{{parameter}}}"""))}}],
              "result": {{result}},
              "canonicalSignature": {
                "parameters": [{{string.Join(",", Enumerable.Repeat("\"i32\"", arity))}}],
                "indirectParameters": {{(indirectParameters ? "true" : "false")}},
                "indirectResult": {{(indirectResult ? "true" : "false")}},
                "flatParameters": [{{string.Join(",", flatParameters.Select(value => $"\"{value}\""))}}]
              }
            }
            """;
        using var document = JsonDocument.Parse(binding);
        var emitter = new RawCanonicalBindingEmitter(wasm64: false);
        return emitter.EmitCallable(0, document.RootElement) + emitter.Functions;
    }
}
