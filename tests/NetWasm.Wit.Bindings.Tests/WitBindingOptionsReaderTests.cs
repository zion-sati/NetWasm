using NetWasm.Wit.Bindings;

namespace NetWasm.Wit.Bindings.Tests;

public sealed class WitBindingOptionsReaderTests
{
#pragma warning disable CA1859 // Contract tests deliberately dispatch through the one-action interface.
    private readonly IWitBindingOptionsReader _reader = new WitBindingOptionsReader();
#pragma warning restore CA1859

    [Fact]
    public void ReadsRequiredAndSelectedOptions()
    {
        var options = _reader.Read([
            "--wit", "contract.wit",
            "--world", "example:test/test",
            "--output", "Bindings.g.cs",
            "--accessibility", "internal",
        ]);

        Assert.Equal("contract.wit", options.Wit);
        Assert.Equal("example:test/test", options.World);
        Assert.Equal("Bindings.g.cs", options.Output);
        Assert.Equal(WitBindingAccessibility.Internal, options.Accessibility);
    }

    [Fact]
    public void RejectsInvalidOptions()
    {
        Assert.Throws<ArgumentNullException>(() => _reader.Read(null!));
        Assert.Contains("specified more than once", Assert.Throws<WitBindingException>(() =>
            _reader.Read([
                "--wit", "first.wit",
                "--wit", "second.wit",
                "--output", "Bindings.g.cs",
            ])).Message);
        Assert.Contains("unknown wit-bindgen option", Assert.Throws<WitBindingException>(() =>
            _reader.Read(["--unknown", "value"])).Message);
        Assert.Contains("unsupported binding accessibility", Assert.Throws<WitBindingException>(() =>
            _reader.Read([
                "--wit", "contract.wit",
                "--output", "Bindings.g.cs",
                "--accessibility", "private",
            ])).Message);
        Assert.Contains("specified more than once", Assert.Throws<WitBindingException>(() =>
            _reader.Read([
                "--wit", "contract.wit",
                "--output", "Bindings.g.cs",
                "--accessibility", "public",
                "--accessibility", "internal",
            ])).Message);
        Assert.Throws<WitBindingException>(() =>
            _reader.Read(["--wit", "contract.wit"]));
        Assert.Throws<WitBindingException>(() =>
            _reader.Read(["--output", "Bindings.g.cs"]));
        Assert.Throws<WitBindingException>(() => _reader.Read(["--wit"]));
    }
}
