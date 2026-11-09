using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Wit.Bindings.TypeDefinitions;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitBindingIntrinsicsSectionWriterTests
{
    // wit-component numbers a function's streams and futures as wit-parser lists them: the
    // parameters, then the result, each type after the ones nested in it. A type used by an
    // import and an export takes the import's built-ins.
    [Fact]
    public void NamesEachTypesBuiltInsByItsFirstFunctionAndPosition()
    {
        var types = ImmutableArray.Create(
            Type(0, """{"stream":"u8"}"""),
            Type(1, """{"stream":"u8"}"""),
            Type(2, """{"future":1}"""),
            Type(3, """{"stream":"s32"}"""));
        var import = new WitFunction(
            "[method]res.pipe",
            [new("data", new WitTypeReference.Defined(0))],
            new WitTypeReference.Defined(2),
            new WitFunctionKind("method"));
        var export = new WitFunction(
            "numbers",
            [new("input", new WitTypeReference.Defined(0))],
            new WitTypeReference.Defined(3),
            new WitFunctionKind("async-freestanding"));
        var document = new WitDocument(
            [],
            [new WitInterface(0, "host", "example:pipes@1.0.0", ImmutableDictionary<string, int>.Empty, [import])],
            [new WitWorld(0, "pipes", "example:pipes@1.0.0",
                [new WitWorldItem("example:pipes/host@1.0.0", 0, null)],
                [new WitWorldItem("numbers", null, export)])],
            types,
            "{}");

        var source = WitBindingWriterFixture.CreateIntrinsics().Generate(document, document.Worlds[0]);

        foreach (var expected in new[]
        {
            "internal sealed class Stream0 : WitStreamIntrinsics",
            "[ComponentBuiltIn(\"example:pipes/host@1.0.0\", \"[stream-new-0][method]res.pipe\")]",
            "[ComponentBuiltIn(\"example:pipes/host@1.0.0\", \"[async-lower][stream-read-1][method]res.pipe\")]",
            "[ComponentBuiltIn(\"example:pipes/host@1.0.0\", \"[future-drop-writable-2][method]res.pipe\")]",
            "internal static global::System.Threading.Tasks.Task<global::System.IO.Stream> Lift(int readable) =>",
            "[ComponentBuiltIn(\"[export]$root\", \"[stream-new-1]numbers\")]",
            "internal static global::System.Collections.Generic.IAsyncEnumerable<int> Lift(int readable) =>",
            "CanonicalAbi.LiftStream(readable, Instance, (nuint)4, (nuint)4, LiftElement);",
            "private static int LiftElement(nuint address)",
        })
        {
            Assert.Contains(expected, source, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("[export]$root\", \"[stream-new-0]numbers", source, StringComparison.Ordinal);
        // A world whose functions use no stream or future needs no intrinsics.
        var plain = document with
        {
            Interfaces = [new WitInterface(0, "host", "example:pipes@1.0.0", ImmutableDictionary<string, int>.Empty, [])],
        };
        Assert.Empty(WitBindingWriterFixture.CreateIntrinsics().Generate(plain, plain.Worlds[0] with { Exports = [] }));
        Assert.Throws<ArgumentNullException>(() => WitBindingWriterFixture.CreateIntrinsics().Generate(null!, document.Worlds[0]));
        Assert.Throws<ArgumentNullException>(() => WitBindingWriterFixture.CreateIntrinsics().Generate(document, null!));
    }

    private static WitTypeDefinition Type(int id, string kind)
    {
        using var json = JsonDocument.Parse(kind);
        return new WitTypeDefinition(id, null, json.RootElement.Clone(), null);
    }
}
