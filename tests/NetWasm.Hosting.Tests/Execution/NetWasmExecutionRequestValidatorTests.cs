using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class NetWasmExecutionRequestValidatorTests
{
    private readonly INetWasmExecutionRequestValidator _subject =
        Assert.IsAssignableFrom<INetWasmExecutionRequestValidator>(new NetWasmExecutionRequestValidator());

    [Theory]
    [InlineData(NetWasmNetworkPolicy.DenyAll)]
    [InlineData(NetWasmNetworkPolicy.AllowAll)]
    public void AcceptsSupportedNetworkPolicies(NetWasmNetworkPolicy network)
    {
        var request = ExecutionRequestFixture.Create();
        _subject.Validate(request with { Grants = request.Grants with { Network = network } });
    }

    [Theory]
    [InlineData(NetWasmPreopenAccess.ReadOnly)]
    [InlineData(NetWasmPreopenAccess.ReadWrite)]
    public void AcceptsSupportedPreopenAccess(NetWasmPreopenAccess access)
    {
        var request = ExecutionRequestFixture.Create();
        _subject.Validate(request with
        {
            Grants = request.Grants with { Preopens = [request.Grants.Preopens[0] with { Access = access }] },
        });
    }

    [Fact]
    public void AcceptsExplicitlyEmptyCollectionsAndRootPreopen()
    {
        var request = ExecutionRequestFixture.Create();
        _subject.Validate(request with
        {
            Arguments = [],
            Environment = [],
            Grants = request.Grants with
            {
                Environment = [],
                Preopens = [request.Grants.Preopens[0] with { GuestPath = "/" }],
                Clocks = [],
            },
            ApplicationImports = [],
        });
    }

    [Fact]
    public void AcceptsUnusedEnvironmentGrantAndCanonicalDotPrefixedSegments()
    {
        var request = ExecutionRequestFixture.Create();
        _subject.Validate(request with
        {
            Environment = [request.Environment[0]],
            Grants = request.Grants with
            {
                Preopens = [request.Grants.Preopens[0] with { GuestPath = "/.config/..cache" }],
            },
        });
    }

    [Fact]
    public void RejectsNullRequest() => Assert.Throws<ArgumentNullException>(() => _subject.Validate(null!));

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public void RejectsUnsafeIncompleteOrAmbiguousRequest(NetWasmExecutionRequest request) =>
        Assert.ThrowsAny<ArgumentException>(() => _subject.Validate(request));

    public static TheoryData<NetWasmExecutionRequest> InvalidRequests()
    {
        var valid = ExecutionRequestFixture.Create();
        var preopen = valid.Grants.Preopens[0];
        var variable = valid.Environment[0];
        var import = valid.ApplicationImports[0];
        var data = new TheoryData<NetWasmExecutionRequest>
        {
            valid with { SchemaVersion = 0 },
            valid with { SchemaVersion = 2 },
            valid with { Arguments = default },
            valid with { Arguments = [null!] },
            valid with { Arguments = ["bad\0argument"] },
            valid with { Grants = null! },
            valid with { Grants = valid.Grants with { Network = (NetWasmNetworkPolicy)99 } },
            valid with { Grants = valid.Grants with { Environment = default } },
            valid with { Grants = valid.Grants with { Preopens = default } },
            valid with { Grants = valid.Grants with { Clocks = default } },
            valid with { Grants = valid.Grants with { Environment = ["ALPHA", "ALPHA"] } },
            valid with { Grants = valid.Grants with { Preopens = [null!] } },
            valid with { Grants = valid.Grants with { Preopens = [preopen with { Access = (NetWasmPreopenAccess)99 }] } },
            valid with { Grants = valid.Grants with { Preopens = [preopen, preopen with { HostPath = Path.GetFullPath("other") }] } },
            valid with { Grants = valid.Grants with { Clocks = [(NetWasmClock)99] } },
            valid with { Grants = valid.Grants with { Clocks = [NetWasmClock.Wall, NetWasmClock.Wall] } },
            valid with { Environment = default },
            valid with { Environment = [null!] },
            valid with { Environment = [variable with { Value = null! }] },
            valid with { Environment = [variable with { Value = "bad\0value" }] },
            valid with { Environment = [variable, variable] },
            valid with { Environment = [variable with { Name = "alpha" }] },
            valid with { ApplicationImports = default },
            valid with { ApplicationImports = [null!] },
            valid with { ApplicationImports = [import with { Module = "wasi:logging/logger@1.0.0" }] },
            valid with { ApplicationImports = [import with { Module = "netwasm:platform/logging@1.0.0" }] },
            valid with { ApplicationImports = [import, import with { ArtifactPath = "imports/other.mjs" }] },
        };

        foreach (var digest in new[] { null, "", new string('a', 63), new string('a', 65), new string('g', 64), new string('A', 64) })
        {
            data.Add(valid with { BuildFingerprint = digest! });
            data.Add(valid with { DeploymentManifestSha256 = digest! });
            data.Add(valid with { ApplicationImports = [import with { Sha256 = digest! }] });
        }

        foreach (var name in new[] { null, "", "BAD=NAME", "BAD\0NAME" })
        {
            data.Add(valid with { Grants = valid.Grants with { Environment = [name!] } });
            data.Add(valid with { Environment = [variable with { Name = name! }] });
        }

        foreach (var hostPath in new[] { null, "", " ", "relative/path", string.Concat(Path.GetFullPath("bad-path"), "\0") })
        {
            data.Add(valid with { Grants = valid.Grants with { Preopens = [preopen with { HostPath = hostPath! }] } });
        }

        foreach (var guestPath in new[] { null, "", " ", "relative", " /padded", "/padded ", "/bad\\path", "/bad\0path", "//", "/a//b", "/.", "/..", "/a/./b", "/a/../b" })
        {
            data.Add(valid with { Grants = valid.Grants with { Preopens = [preopen with { GuestPath = guestPath! }] } });
        }

        foreach (var path in new[] { null, "", " ", "/rooted.mjs", "C:/rooted.mjs", "nested\\file.mjs", "a//b", "./app.mjs", "a/../b", "bad\0path", " padded.mjs", "padded.mjs " })
        {
            data.Add(valid with { ApplicationImports = [import with { ArtifactPath = path! }] });
        }

        foreach (var module in new[] { null, "", " ", "@1.0.0", "module@", "a@b@1.0.0", "Module@1.0.0", "bad_module@1.0.0", ":bad@1.0.0", "module@1.0", "module@1..0", "module@1.x.0", "bad\0module@1.0.0", " module@1.0.0", "module@1.0.0 " })
        {
            data.Add(valid with { ApplicationImports = [import with { Module = module! }] });
        }

        return data;
    }
}
