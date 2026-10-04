using System.Text;

namespace NetWasm.Toolchain.Tests;

public sealed class JcoPatchArtifactTests
{
    [Theory]
    [InlineData("/" + "Users/")]
    [InlineData("/" + "home/")]
    [InlineData(@"C:\" + "Users\\")]
    public void PatchedBindgenModuleContainsNoBuildMachineHomePath(string prefix)
    {
        var repository = FindRepositoryRoot();
        var module = File.ReadAllBytes(Path.Combine(repository, "eng", "jco-patches",
            "1.28.1-netwasm.2", "js-component-bindgen-component.core.wasm"));

        Assert.Equal(-1, module.AsSpan().IndexOf(Encoding.UTF8.GetBytes(prefix)));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "NetWasm.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException(
            "The NetWasm repository root was not found.");
    }
}
