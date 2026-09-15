using System.Collections.Immutable;
using NetWasm.Compiler.Browser.Inputs;
using NetWasm.Compiler.ExceptionTypes;

namespace NetWasm.Compiler.Browser.Tests.Inputs;

public sealed class VirtualCompilationInputHasherTests
{
    [Fact]
    public void HashesOriginalWitBytesIndependentlyOfNormalizedJson()
    {
        var options = BrowserCompilationRequestTests.CreateOptions();
        var inputs = new Dictionary<string, byte[]> { ["contract.wit.wasm"] = [97, 98, 99] };
        var first = new BrowserCompilationRequest(options, inputs,
            new Dictionary<string, string> { ["contract.wit.wasm"] = "first JSON" });
        var second = new BrowserCompilationRequest(options, inputs,
            new Dictionary<string, string> { ["contract.wit.wasm"] = "different JSON" });
        var firstHasher = CreateHasher(first.Inputs);
        var secondHasher = CreateHasher(second.Inputs);

        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            firstHasher.Hash("contract.wit.wasm"));
        Assert.Equal(firstHasher.Hash("contract.wit.wasm"), secondHasher.Hash("contract.wit.wasm"));
        inputs["contract.wit.wasm"][0] = 0;
        var changed = new BrowserCompilationRequest(options, inputs,
            new Dictionary<string, string> { ["contract.wit.wasm"] = "first JSON" });
        Assert.NotEqual(firstHasher.Hash("contract.wit.wasm"), CreateHasher(changed.Inputs).Hash("contract.wit.wasm"));
    }

    [Fact]
    public void HashesEmptySemanticInputs()
    {
        var hasher = CreateHasher(ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add("empty", []));

        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
            hasher.Hash("empty"));
    }

    [Fact]
    public void RequiresExactSuppliedPathsAndInitializedInputs()
    {
        Assert.Throws<ArgumentNullException>(() => CreateHasher(null!));
        var hasher = CreateHasher(ImmutableDictionary<string, ImmutableArray<byte>>.Empty.Add("folder/input", [1]));
        var missing = Assert.Throws<FileNotFoundException>(() => hasher.Hash("input"));
        Assert.Equal("input", missing.FileName);
        var wrongCase = Assert.Throws<FileNotFoundException>(() => hasher.Hash("folder/INPUT"));
        Assert.Equal("folder/INPUT", wrongCase.FileName);
        Assert.Throws<ArgumentNullException>(() => hasher.Hash(null!));
        Assert.Throws<ArgumentException>(() => hasher.Hash(" "));
    }

    private static ICompilationInputHasher CreateHasher(
        ImmutableDictionary<string, ImmutableArray<byte>> inputs) =>
        Assert.IsAssignableFrom<ICompilationInputHasher>(new VirtualCompilationInputHasher(inputs));
}
