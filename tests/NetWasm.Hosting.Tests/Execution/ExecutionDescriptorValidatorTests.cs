using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

public sealed class ExecutionDescriptorValidatorTests
{
    private readonly IExecutionDescriptorValidator _subject = Assert.IsAssignableFrom<IExecutionDescriptorValidator>(new ExecutionDescriptorValidator());

    [Fact]
    public void AcceptsCompleteLocalDescriptorWithoutAccessingFiles() => _subject.Validate(DescriptorFixture.Create());

    [Fact]
    public void RejectsNullDescriptor() => Assert.Throws<ArgumentNullException>(() => _subject.Validate(null!));

    [Theory]
    [MemberData(nameof(InvalidDescriptors))]
    public void RejectsIncompleteAmbiguousOrUnsupportedDescriptors(ExecutionDescriptor descriptor) =>
        Assert.ThrowsAny<ArgumentException>(() => _subject.Validate(descriptor));

    public static TheoryData<ExecutionDescriptor> InvalidDescriptors()
    {
        var valid = DescriptorFixture.Create();
        var package = valid.ToolPackages[0];
        var data = new TheoryData<ExecutionDescriptor>
        {
            valid with { SchemaVersion = 0 },
            valid with { SchemaVersion = 2 },
            valid with { HostingVersion = null! },
            valid with { HostingVersion = " " },
            valid with { ToolPackages = default },
            valid with { ToolPackages = [] },
            valid with { ToolPackages = [null!] },
            valid with { ToolPackages = [package, package with { Version = "other" }] },
            valid with { ToolPackages = [package, package with { Id = package.Id.ToUpperInvariant() }] },
            valid with { ToolPackages = [package with { Id = null! }] },
            valid with { ToolPackages = [package with { Id = " " }] },
            valid with { ToolPackages = [package with { Version = null! }] },
            valid with { ToolPackages = [package with { Version = "" }] },
        };
        foreach (var digest in new[] { null, "", new string('a', 63), new string('a', 65), new string('g', 64), new string('A', 64) })
        {
            data.Add(valid with { BuildFingerprint = digest! });
            data.Add(valid with { DeploymentManifestSha256 = digest! });
            data.Add(valid with { ToolPackages = [package with { Sha256 = digest! }] });
        }

        foreach (var path in new[] { null, "", " ", "relative/path", valid.LauncherPath + '\0' })
        {
            data.Add(valid with { DeploymentManifestPath = path! });
            data.Add(valid with { HostExecutablePath = path! });
            data.Add(valid with { LauncherPath = path! });
            data.Add(valid with { ToolPackages = [package with { RootPath = path! }] });
        }

        return data;
    }
}
