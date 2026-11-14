using Microsoft.Build.Utilities;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class CompilerReferenceOriginReaderTests
{
    [Theory]
    [InlineData(CompilationReferenceRole.Implementation)]
    [InlineData(CompilationReferenceRole.Metadata)]
    public void ReadPreservesResolvedPackageIdentityAndViewProvenance(CompilationReferenceRole role)
    {
        var item = new TaskItem("generated-view.dll");
        item.SetMetadata("NuGetPackageId", "Newtonsoft.Json");
        item.SetMetadata("NuGetPackageVersion", "13.0.3");
        item.SetMetadata("Version", "13.0.0.0");
        item.SetMetadata("PathInPackage", "lib/netstandard2.0/Newtonsoft.Json.dll");
        item.SetMetadata("NetWasmOriginalReference", "original.dll");
        var reader = Assert.IsAssignableFrom<ICompilerReferenceOriginReader>(new CompilerReferenceOriginReader());

        var origin = Assert.Single(reader.Read([item], role));

        Assert.Equal(new CompilationReferenceOrigin("generated-view.dll", role, "Newtonsoft.Json", "13.0.3",
            "lib/netstandard2.0/Newtonsoft.Json.dll", "original.dll"), origin);
        Assert.Equal("13.0.0.0", item.GetMetadata("Version"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ReadDoesNotInventPackageMetadataForDirectReferences(string value)
    {
        var item = new TaskItem("direct.dll");
        foreach (var name in new[] { "NuGetPackageId", "NuGetPackageVersion", "PathInPackage", "NetWasmOriginalReference" })
            item.SetMetadata(name, value);
        var reader = Assert.IsAssignableFrom<ICompilerReferenceOriginReader>(new CompilerReferenceOriginReader());

        Assert.Equal(new CompilationReferenceOrigin("direct.dll", CompilationReferenceRole.Implementation),
            Assert.Single(reader.Read([item], CompilationReferenceRole.Implementation)));
        Assert.Empty(reader.Read([], CompilationReferenceRole.Metadata));
        Assert.Throws<ArgumentNullException>(() => reader.Read(null!, CompilationReferenceRole.Metadata));
    }
}
