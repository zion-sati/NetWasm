using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeValidationProfileValidatorTests
{
    private readonly IRuntimeNativeValidationProfileValidator _validator = Assert.IsAssignableFrom<IRuntimeNativeValidationProfileValidator>(
        new RuntimeNativeValidationProfileValidator());

    [Theory]
    [InlineData("wasm32")]
    [InlineData("wasm64")]
    public void AcceptsExplicitTargetFeaturesAndScalarImportContracts(string target)
    {
        var profile = RuntimePackTestData.NativeProfile(target);
        _validator.Validate(profile, target);
        _validator.Validate(profile with { Imports = [profile.Imports[0] with { Parameters = [0x7f, 0x7e, 0x7d, 0x7c], Results = [0x7f] }] }, target);
    }

    [Fact]
    public void RejectsUnsupportedFeaturesAndTargetMismatch()
    {
        var profile = RuntimePackTestData.NativeProfile();
        Assert.Throws<ArgumentNullException>(() => _validator.Validate(null!, "wasm32"));
        Assert.Throws<InvalidOperationException>(() => _validator.Validate(profile, "wasm16"));
        Assert.Throws<InvalidOperationException>(() => _validator.Validate(profile, "wasm64"));
        foreach (var invalid in new[]
        {
            profile with { Version = 2 }, profile with { Features = default }, profile with { Features = [] },
            profile with { Features = ["all"] }, profile with { Features = ["bulk-memory", "mvp"] },
            profile with { Features = profile.Features.Add("simd") }, profile with { Features = profile.Features.Add("threads") },
            profile with { Features = profile.Features.Add("exceptions") }, profile with { Features = profile.Features.Add("multi-memory") },
            profile with { Features = profile.Features.Add("tail-call") }, profile with { Features = profile.Features.Add(null!) },
            profile with { Features = profile.Features.Add("mvp") }, profile with { Features = profile.Features.Add("memory64") },
        })
            Assert.Throws<InvalidOperationException>(() => _validator.Validate(invalid, "wasm32"));
    }

    [Fact]
    public void RejectsMissingAmbiguousOrNonScalarImportContracts()
    {
        var profile = RuntimePackTestData.NativeProfile();
        var contract = profile.Imports[0];
        foreach (var invalid in new[]
        {
            profile with { Imports = default }, profile with { Imports = [] },
            profile with { Imports = [null!] }, profile with { Imports = [contract with { Module = " " }] },
            profile with { Imports = [contract with { Name = "" }] },
            profile with { Imports = [contract with { Parameters = default }] },
            profile with { Imports = [contract with { Results = default }] },
            profile with { Imports = [contract with { Parameters = [0x70] }] },
            profile with { Imports = [contract with { Results = [0x7b] }] }, profile with { Imports = [contract, contract] },
        })
            Assert.Throws<InvalidOperationException>(() => _validator.Validate(invalid, "wasm32"));
        foreach (var control in new[] { '\0', '\r', '\n' })
        {
            Assert.Throws<InvalidOperationException>(() => _validator.Validate(profile with
            { Imports = [contract with { Module = "env" + control }] }, "wasm32"));
            Assert.Throws<InvalidOperationException>(() => _validator.Validate(profile with
            { Imports = [contract with { Name = "name" + control }] }, "wasm32"));
        }
    }
}
