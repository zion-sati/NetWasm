namespace NetWasm.Sdk.Pack.Tests;

public sealed class NerdbankDependencyRegressionTests
{
    [Fact]
    [Trait("Issue", "53")]
    public void CompilePrivateDependencyRetainsItsRuntimeDependency()
    {
        var validator = new[] { new DependencyPolicy() }.Cast<IDependencyValidator>().Single();
        var dependency = validator.Validate(new CanonicalPackageDependencyInput(
            "Example.Dependency", "1.2.3", CanonicalPackPlanBuilder.CanonicalTargetFramework)
        {
            PrivateAssets = "compile",
        }, TargetProfile.NetWasmV01);
        Assert.Equal("Example.Dependency", dependency.Id);
        Assert.Equal("1.2.3", dependency.VersionRange);
        Assert.Equal("compile", dependency.PrivateAssets);
        Assert.Equal("compile", dependency.ExcludeAssets);
    }
}
