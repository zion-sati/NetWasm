using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Raw;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests.Raw;

public sealed class RawPayloadImportLayoutBuilderTests
{
    private static readonly WitFunction Send = new("send", [], null, new("freestanding"));

    [Theory]
    [InlineData(RawPayloadOperation.StreamNew, 0, "[stream-new-0]send", "", "I8")]
    [InlineData(RawPayloadOperation.StreamRead, 0, "[async-lower][stream-read-0]send", "I4,ManagedAddress,ManagedAddress", "I4")]
    [InlineData(RawPayloadOperation.StreamWrite, 0, "[async-lower][stream-write-0]send", "I4,ManagedAddress,ManagedAddress", "I4")]
    [InlineData(RawPayloadOperation.StreamDropReadable, 0, "[stream-drop-readable-0]send", "I4", "Void")]
    [InlineData(RawPayloadOperation.StreamDropWritable, 0, "[stream-drop-writable-0]send", "I4", "Void")]
    [InlineData(RawPayloadOperation.FutureNew, 1, "[future-new-1]send", "", "I8")]
    [InlineData(RawPayloadOperation.FutureRead, 1, "[async-lower][future-read-1]send", "I4,ManagedAddress", "I4")]
    [InlineData(RawPayloadOperation.FutureWrite, 1, "[async-lower][future-write-1]send", "I4,ManagedAddress", "I4")]
    [InlineData(RawPayloadOperation.FutureDropReadable, 1, "[future-drop-readable-1]send", "I4", "Void")]
    [InlineData(RawPayloadOperation.FutureDropWritable, 1, "[future-drop-writable-1]send", "I4", "Void")]
    public void PlansEachBuiltInsNameAndFixedCoreSignature(
        RawPayloadOperation operation, int index, string name, string parameters, string result)
    {
        var declaration = new RawWitImportDeclaration.Payload(
            "sample:io/api@0.3.2", "sample:io/api@0.3.2", Send, index, index, operation);

        var layout = Assert.IsType<RawWitImportLayout.Payload>(Create().Build(new(Document(), declaration, WasmTarget.Wasm64)));

        Assert.Equal(new RawCanonicalImportIdentity("sample:io/api@0.3.2", name), layout.Identity);
        Assert.Equal(WasmTarget.Wasm64, layout.Target);
        Assert.Same(declaration, layout.Declaration);
        Assert.Equal(index == 0 ? CanonicalAbiTypeKind.Stream : CanonicalAbiTypeKind.Future, layout.Type.Kind);
        Assert.Equal(parameters, string.Join(",", layout.Signature.Parameters));
        Assert.Equal(parameters, string.Join(",", layout.Signature.FlatParameters));
        Assert.Equal(Enum.Parse<CliValueKind>(result), layout.Signature.Result);
        Assert.Equal(result == "Void" ? "" : result, string.Join(",", layout.Signature.FlatResults));
        Assert.False(layout.Signature.IndirectParameters || layout.Signature.IndirectResult);
    }

    [Fact]
    public void AWorldsOwnFunctionNamesItsBuiltInsByTheRootModule()
    {
        var declaration = new RawWitImportDeclaration.Payload("", "", Send, 0, 0, RawPayloadOperation.StreamNew);

        Assert.Equal(new RawCanonicalImportIdentity("$root", "[stream-new-0]send"),
            Create().Build(new(Document(), declaration, WasmTarget.Wasm32)).Identity);
    }

    [Theory]
    [InlineData(RawPayloadOperation.FutureNew, 0)]
    [InlineData(RawPayloadOperation.StreamRead, 1)]
    [InlineData(RawPayloadOperation.StreamNew, 2)]
    public void RejectsABuiltInThatDoesNotMatchItsType(RawPayloadOperation operation, int typeId)
    {
        var declaration = new RawWitImportDeclaration.Payload("", "", Send, 0, typeId, operation);

        Assert.Throws<CompilerException>(() => Create().Build(new(Document(), declaration, WasmTarget.Wasm32)));
    }

    [Fact]
    public void RejectsAnotherDeclarationAndMissingDependencies()
    {
        Assert.Throws<CompilerException>(() => Create().Build(new(
            Document(), new RawWitImportDeclaration.Callable("", Send), WasmTarget.Wasm32)));
        Assert.Throws<ArgumentNullException>(() => Create().Build(null!));
        Assert.Throws<ArgumentNullException>(() => new RawPayloadImportLayoutBuilder(null!));
    }

    private static RawPayloadImportLayoutBuilder Create() => new(new WitCanonicalTypeResolver());

    private static WitDocument Document() => new([], [], [],
        [Type(0, """{"stream":"u8"}"""), Type(1, """{"future":null}"""), Type(2, """{"list":"u8"}""")], "{}");

    private static WitTypeDefinition Type(int id, string json)
    {
        using var document = JsonDocument.Parse(json);
        return new(id, null, document.RootElement.Clone(), null);
    }
}
