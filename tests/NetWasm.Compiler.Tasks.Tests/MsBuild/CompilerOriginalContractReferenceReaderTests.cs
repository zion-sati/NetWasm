using Microsoft.Build.Utilities;
using NetWasm.Compiler.Tasks.MsBuild;

namespace NetWasm.Compiler.Tasks.Tests.MsBuild;

public sealed class CompilerOriginalContractReferenceReaderTests
{
    [Fact]
    public void ReadSelectsOnlyExplicitOriginalPathsWithoutMutatingItems()
    {
        var first = new TaskItem("view.dll");
        first.SetMetadata("NetWasmOriginalReference", "original.dll");
        var duplicate = new TaskItem("other-view.dll");
        duplicate.SetMetadata("NetWasmOriginalReference", "original.dll");
        var distinct = new TaskItem("third-view.dll");
        distinct.SetMetadata("NetWasmOriginalReference", "Original.dll");
        var blank = new TaskItem("blank.dll");
        blank.SetMetadata("NetWasmOriginalReference", "  ");
        var direct = new TaskItem("direct.dll");
        direct.SetMetadata("NuGetPackageId", "NotAContractPath");
        var reader = Assert.IsAssignableFrom<ICompilerOriginalContractReferenceReader>(
            new CompilerOriginalContractReferenceReader());

        Assert.Equal(["original.dll", "Original.dll"],
            reader.Read([first, duplicate, distinct, blank, direct]).ToArray());
        Assert.Equal("view.dll", first.ItemSpec);
        Assert.Equal("original.dll", first.GetMetadata("NetWasmOriginalReference"));
        Assert.Empty(reader.Read([]));
        Assert.Throws<ArgumentNullException>(() => reader.Read(null!));
    }
}
