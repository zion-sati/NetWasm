using System.Collections.Immutable;
using NetWasm.Compiler.Browser.Inputs;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Browser.Tests.Inputs;

public sealed class VirtualManagedAssemblyImageReaderTests
{
    [Fact]
    public void ReadsTheActiveSessionRequest()
    {
        var resolver = FixedBrowserCompilationRequestResolver.ForInputs(
            ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add("app.dll", [4, 2]));
        var reader = new VirtualManagedAssemblyImageReader(resolver);

        Assert.Equal(new byte[] { 4, 2 }, reader.Read("app.dll"));
    }

    [Fact]
    public void ReadsExactInputsAndReturnsIndependentOwnedArrays()
    {
        var request = new BrowserCompilationRequest(BrowserCompilationRequestTests.CreateOptions(),
            new Dictionary<string, byte[]> { ["refs/core.dll"] = [1, 2, 3] },
            new Dictionary<string, string>());
        IManagedAssemblyImageReader reader = CreateReader(request.Inputs);

        var first = reader.Read("refs/core.dll");
        first[0] = 9;

        Assert.Equal(new byte[] { 1, 2, 3 }, reader.Read("refs/core.dll"));
    }

    [Theory]
    [InlineData("core.dll")]
    [InlineData("refs/CORE.dll")]
    [InlineData("missing.dll")]
    public void MissingInputsNeverFallBackToAnotherPath(string path)
    {
        var request = new BrowserCompilationRequest(BrowserCompilationRequestTests.CreateOptions(),
            new Dictionary<string, byte[]> { ["refs/core.dll"] = [1] },
            new Dictionary<string, string>());
        var reader = CreateReader(request.Inputs);

        var failure = Assert.Throws<FileNotFoundException>(() => reader.Read(path));

        Assert.Equal(path, failure.FileName);
    }

    [Fact]
    public void RequiresInitializedInputsAndValidPaths()
    {
        Assert.Throws<ArgumentNullException>(() => CreateReader(null!));
        Assert.Throws<ArgumentNullException>(() => new VirtualManagedAssemblyImageReader(
            (IBrowserCompilationRequestResolver)null!));
        var reader = CreateReader(ImmutableDictionary<string, ImmutableArray<byte>>.Empty);
        Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
        Assert.Throws<ArgumentException>(() => reader.Read(" "));
    }

    private static IManagedAssemblyImageReader CreateReader(
        ImmutableDictionary<string, ImmutableArray<byte>> inputs) =>
        Assert.IsAssignableFrom<IManagedAssemblyImageReader>(new VirtualManagedAssemblyImageReader(inputs));
}
