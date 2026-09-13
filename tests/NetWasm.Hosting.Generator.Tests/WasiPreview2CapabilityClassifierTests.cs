using NetWasm.Hosting.Capabilities;
using NetWasm.Hosting.Generator;

namespace NetWasm.Hosting.Generator.Tests;

public sealed class WasiPreview2CapabilityClassifierTests
{
    public static TheoryData<NetWasmPlatformCapability, string[]> Policies => new()
    {
        {
            NetWasmPlatformCapability.Baseline,
            [
                "wasi:cli/exit@0.2.11", "wasi:cli/stderr@0.2.11",
                "wasi:cli/stdin@0.2.11", "wasi:cli/stdout@0.2.11",
                "wasi:cli/terminal-input@0.2.11", "wasi:cli/terminal-output@0.2.11",
                "wasi:cli/terminal-stderr@0.2.11", "wasi:cli/terminal-stdin@0.2.11",
                "wasi:cli/terminal-stdout@0.2.11", "wasi:io/error@0.2.11",
                "wasi:io/poll@0.2.11", "wasi:io/streams@0.2.11",
            ]
        },
        { NetWasmPlatformCapability.Environment, ["wasi:cli/environment@0.2.11"] },
        { NetWasmPlatformCapability.MonotonicClock, ["wasi:clocks/monotonic-clock@0.2.11"] },
        { NetWasmPlatformCapability.WallClock, ["wasi:clocks/wall-clock@0.2.11"] },
        {
            NetWasmPlatformCapability.PreopenedDirectories,
            ["wasi:filesystem/preopens@0.2.11", "wasi:filesystem/types@0.2.11"]
        },
        {
            NetWasmPlatformCapability.Randomness,
            [
                "wasi:random/insecure-seed@0.2.11", "wasi:random/insecure@0.2.11",
                "wasi:random/random@0.2.11",
            ]
        },
        {
            NetWasmPlatformCapability.Network,
            [
                "wasi:sockets/instance-network@0.2.11",
                "wasi:sockets/ip-name-lookup@0.2.11", "wasi:sockets/network@0.2.11",
                "wasi:sockets/tcp-create-socket@0.2.11", "wasi:sockets/tcp@0.2.11",
                "wasi:sockets/udp-create-socket@0.2.11", "wasi:sockets/udp@0.2.11",
                "wasi:http/incoming-handler@0.2.11",
                "wasi:http/outgoing-handler@0.2.11", "wasi:http/types@0.2.11",
            ]
        },
    };

    [Theory]
    [MemberData(nameof(Policies))]
    public void ClassifiesEveryOfficialCommandInterfaceExplicitly(
        NetWasmPlatformCapability expected,
        string[] modules)
    {
        var classifier = new WasiPreview2CapabilityClassifier();

        Assert.All(modules, module => Assert.Equal(expected, classifier.Classify(module)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingModules(string module)
    {
        Assert.Throws<ArgumentException>(() =>
            new WasiPreview2CapabilityClassifier().Classify(module));
    }

    [Fact]
    public void RejectsUnknownOrVersionDriftedInterfaces()
    {
        var classifier = new WasiPreview2CapabilityClassifier();

        Assert.Throws<InvalidDataException>(() =>
            classifier.Classify("wasi:http/types@0.2.12"));
        Assert.Throws<InvalidDataException>(() =>
            classifier.Classify("wasi:cli/environment@0.2.12"));
    }
}
