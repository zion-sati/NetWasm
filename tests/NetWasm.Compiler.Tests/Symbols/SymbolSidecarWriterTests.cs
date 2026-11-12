using NetWasm.Compiler.Core;
using NetWasm.Compiler.Symbols;

namespace NetWasm.Compiler.Tests.Symbols;

public sealed class SymbolSidecarWriterTests
{
    private static readonly string BuildId = new('a', 32);

    [Fact]
    public void WritesTheHeaderAndEntriesInIdOrderSharingRepeatedStrings()
    {
        var artifact = new SymbolSidecarWriter().Write(SymbolSidecarKind.Type, BuildId,
        [
            new(9, ["Order", "Shop", "Shop.Order", "Shop.Order"]),
            new(3, ["Global", null, "Global", "Global"]),
        ]);

        var contents = SymbolSidecarContents.Read(artifact.Bytes);
        Assert.Equal(SymbolSidecarKind.Type, artifact.Kind);
        Assert.Equal(SymbolSidecarArtifact.TypeSymbolsMediaType, artifact.MediaType);
        Assert.Equal((SymbolSidecarWriter.Magic, SymbolSidecarWriter.Version, (ushort)1, BuildId),
            (contents.Magic, contents.Version, contents.Kind, contents.BuildId));
        Assert.Equal([3, 9], contents.Entries.Select(entry => entry.Id));
        Assert.Equal<IEnumerable<string?>>(["Global", null, "Global", "Global"], contents.Entries[0].Strings);
        Assert.Equal<IEnumerable<string?>>(["Order", "Shop", "Shop.Order", "Shop.Order"], contents.Entries[1].Strings);
        // Each distinct string's characters are stored once: Global, Order, Shop, Shop.Order.
        Assert.Equal(SymbolSidecarWriter.HeaderSize + 2 * 36 + "GlobalOrderShopShop.Order".Length * 2,
            artifact.Bytes.Length);
    }

    [Fact]
    public void WritesMethodSidecarsWithOneStringPerEntry()
    {
        var artifact = new SymbolSidecarWriter().Write(SymbolSidecarKind.Method, BuildId, [new(1, ["App.Program::Main"])]);

        Assert.Equal(SymbolSidecarArtifact.MethodSymbolsMediaType, artifact.MediaType);
        var entry = Assert.Single(SymbolSidecarContents.Read(artifact.Bytes).Entries);
        Assert.Equal((1, "App.Program::Main"), (entry.Id, entry.Strings[0]));
        Assert.Single(entry.Strings);
    }

    // The hosts' reader test (symbol-sidecar-reader.test.mjs) reads these exact bytes.
    [Fact]
    public void WritesTheGoldenMethodSidecar() =>
        Assert.Equal(
            "4E5753590100020061006100610061006100610061006100610061006100610061006100610061006100610061006100"
            + "610061006100610061006100610061006100610061006100010000000100000001000000000000000200000048006900",
            Convert.ToHexString(new SymbolSidecarWriter().Write(SymbolSidecarKind.Method, BuildId, [new(1, ["Hi"])]).Bytes));

    [Fact]
    public void TheSameEntriesInAnyOrderWriteTheSameBytes() =>
        Assert.Equal(
            new SymbolSidecarWriter().Write(SymbolSidecarKind.Method, BuildId, [new(1, ["a"]), new(2, ["b"])]).Bytes,
            new SymbolSidecarWriter().Write(SymbolSidecarKind.Method, BuildId, [new(2, ["b"]), new(1, ["a"])]).Bytes);

    [Fact]
    public void RejectsWhatTheFormatCannotHold()
    {
        var writer = new SymbolSidecarWriter();

        Assert.Throws<ArgumentNullException>(() => writer.Write(SymbolSidecarKind.Type, null!, []));
        Assert.Throws<ArgumentException>(() => writer.Write(SymbolSidecarKind.Type, BuildId, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => writer.Write((SymbolSidecarKind)3, BuildId, []));
        Assert.Equal(DiagnosticCode.RuntimeContract, Assert.Throws<CompilerException>(() =>
            writer.Write(SymbolSidecarKind.Type, "short", [])).Diagnostic.Code);
        Assert.Equal(DiagnosticCode.RuntimeContract, Assert.Throws<CompilerException>(() =>
            writer.Write(SymbolSidecarKind.Method, BuildId, [new(1, ["a", "b"])])).Diagnostic.Code);
        Assert.Equal(DiagnosticCode.RuntimeContract, Assert.Throws<CompilerException>(() =>
            writer.Write(SymbolSidecarKind.Method, BuildId, [new(1, ["a"]), new(1, ["b"])])).Diagnostic.Code);
    }
}
