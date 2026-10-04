using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeValidationArgumentBuilderTests
{
    private readonly ProfileStub _profiles = new();
    private IRuntimeNativeValidationArgumentBuilder CreateBuilder() => Assert.IsAssignableFrom<IRuntimeNativeValidationArgumentBuilder>(
        new RuntimeNativeValidationArgumentBuilder(_profiles));

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void ResetsDefaultFeaturesAndEnablesOnlyTheExplicitTargetProfile(string target)
    {
        var profile = RuntimePackTestData.NativeProfile(target);
        var path = Path.GetFullPath("a file with spaces.wasm");
        var result = CreateBuilder().Build(profile, target, path);

        Assert.Equal(["validate", path, "--features=" + string.Join(',', profile.Features)], result.ToArray());
        Assert.StartsWith("--features=mvp,", result[2], StringComparison.Ordinal);
        Assert.Equal(target == "wasm64", result[2].Contains("memory64", StringComparison.Ordinal));
        Assert.Equal((profile, target), Assert.Single(_profiles.Requests));
    }

    [Fact]
    public void RejectsUnsupportedOrUninitializedProfilesBeforeProducingArguments()
    {
        var profile = RuntimePackTestData.NativeProfile();
        Assert.Throws<ArgumentNullException>(() => new RuntimeNativeValidationArgumentBuilder(null!));
        var path = Path.GetFullPath("module.wasm");
        var builder = CreateBuilder();
        Assert.Throws<ArgumentException>(() => builder.Build(profile, "wasm32", " "));
        Assert.Empty(_profiles.Requests);
        Assert.Throws<InvalidOperationException>(() => builder.Build(profile, "wasm32", "relative.wasm"));
        _profiles.Failure = new InvalidOperationException("invalid profile");
        Assert.Same(_profiles.Failure, Assert.Throws<InvalidOperationException>(() => builder.Build(profile, "wasm32", path)));
    }

    private sealed class ProfileStub : IRuntimeNativeValidationProfileValidator
    {
        public List<(RuntimeNativeValidationProfile Profile, string Target)> Requests { get; } = [];
        public Exception? Failure { get; set; }
        public void Validate(RuntimeNativeValidationProfile profile, string target)
        {
            Requests.Add((profile, target));
            if (Failure is not null) throw Failure;
        }
    }
}
