using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Generator;

namespace NetWasm.Hosting.Generator.Tests;

public sealed class WasiCapabilityClassifierTests
{
    public static TheoryData<NetWasmPlatformCapability, string[]> Policies => new()
    {
        {
            NetWasmPlatformCapability.Baseline,
            [
                "wasi:cli/exit@0.3.2", "wasi:cli/stderr@0.3.2",
                "wasi:cli/stdin@0.3.2", "wasi:cli/stdout@0.3.2",
                "wasi:cli/terminal-input@0.3.2", "wasi:cli/terminal-output@0.3.2",
                "wasi:cli/terminal-stderr@0.3.2", "wasi:cli/terminal-stdin@0.3.2",
                "wasi:cli/terminal-stdout@0.3.2", "wasi:cli/types@0.3.2",
                "wasi:clocks/types@0.3.2",
            ]
        },
        { NetWasmPlatformCapability.Environment, ["wasi:cli/environment@0.3.2"] },
        { NetWasmPlatformCapability.MonotonicClock, ["wasi:clocks/monotonic-clock@0.3.2"] },
        { NetWasmPlatformCapability.WallClock, ["wasi:clocks/system-clock@0.3.2"] },
        {
            NetWasmPlatformCapability.PreopenedDirectories,
            ["wasi:filesystem/preopens@0.3.2", "wasi:filesystem/types@0.3.2"]
        },
        {
            NetWasmPlatformCapability.Randomness,
            [
                "wasi:random/insecure-seed@0.3.2", "wasi:random/insecure@0.3.2",
                "wasi:random/random@0.3.2",
            ]
        },
        {
            NetWasmPlatformCapability.Network,
            [
                "wasi:sockets/ip-name-lookup@0.3.2", "wasi:sockets/types@0.3.2",
                "wasi:http/client@0.3.2", "wasi:http/types@0.3.2",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(Policies))]
    public void ClassifiesEveryOfficialCommandInterfaceExplicitly(
        NetWasmPlatformCapability expected,
        string[] modules)
    {
        var classifier = new WasiCapabilityClassifier();

        Assert.All(modules, module => Assert.Equal(expected, classifier.Classify(module)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingModules(string module)
    {
        Assert.Throws<ArgumentException>(() =>
            new WasiCapabilityClassifier().Classify(module));
    }

    [Fact]
    public void RejectsUnknownOrVersionDriftedInterfaces()
    {
        var classifier = new WasiCapabilityClassifier();

        Assert.Throws<InvalidDataException>(() =>
            classifier.Classify("wasi:http/types@0.3.3"));
        Assert.Throws<InvalidDataException>(() =>
            classifier.Classify("wasi:cli/environment@0.2.11"));
    }
}
