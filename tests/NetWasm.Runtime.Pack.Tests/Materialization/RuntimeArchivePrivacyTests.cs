using System.Diagnostics;
using System.Text;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeArchivePrivacyTests
{
    [Theory]
    [InlineData("tmpnam")]
    [InlineData("tmpfile")]
    public void PortableTemporaryFileTemplatesDoNotHideBuildPaths(string name)
    {
        var template = string.Concat("/", "tmp", "/", name, "_XXXXXX");
        Assert.Equal(string.Empty, RemovePortableTemporaryFileTemplates(template + "\0"));
        Assert.Equal(template + "/build\0",
            RemovePortableTemporaryFileTemplates(template + "/build\0"));
        var buildPath = string.Concat("/", "tmp", "/netwasm-build/source.c\0");
        Assert.Equal(buildPath, RemovePortableTemporaryFileTemplates(buildPath));
    }

    private static string RemovePortableTemporaryFileTemplates(string text) => text
        .Replace(string.Concat("/", "tmp", "/tmpnam_XXXXXX\0"), string.Empty, StringComparison.Ordinal)
        .Replace(string.Concat("/", "tmp", "/tmpfile_XXXXXX\0"), string.Empty, StringComparison.Ordinal);

    [Fact]
    public void NormalizerReplacesBuildRootsWithoutChangingArchiveLength()
    {
        using var directory = new TemporaryDirectory();
        var archive = directory.Write(
            "runtime.a",
            Encoding.UTF8.GetBytes("header:/private/contributor/source:file"));
        var before = File.ReadAllBytes(archive);
        var root = FindRepositoryRoot();
        var script = Path.Combine(
            root,
            "src",
            "NetWasm.Runtime.Pack",
            "tools",
            "normalize-archive-paths.mjs");

        using var process = Process.Start(new ProcessStartInfo("node")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList =
            {
                script,
                "--prefix",
                "/private/contributor",
                "--archive",
                archive,
            },
        }) ?? throw new InvalidOperationException("Node did not start.");
        process.WaitForExit();
        var standardError = process.StandardError.ReadToEnd();
        var after = File.ReadAllBytes(archive);

        Assert.Equal(0, process.ExitCode);
        Assert.Equal(string.Empty, standardError);
        Assert.Equal(before.Length, after.Length);
        Assert.DoesNotContain(
            "/private/contributor",
            Encoding.UTF8.GetString(after),
            StringComparison.Ordinal);
    }

    [Fact]
    public void PackagedArchivesContainNoMachineLocalBuildRoots()
    {
        var runtimeRoot = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "NetWasm.Runtime.Pack",
            "runtime");
        var forbidden = new[]
        {
            Encoding.UTF8.GetBytes(string.Concat("/", "Users", "/")),
            Encoding.UTF8.GetBytes(string.Concat("/", "home", "/")),
            Encoding.UTF8.GetBytes(string.Concat("/", "tmp", "/")),
        };

        foreach (var archive in Directory.EnumerateFiles(
                     runtimeRoot,
                     "*.a",
                     SearchOption.AllDirectories))
        {
            // libc retains portable temporary-file templates as runtime data.
            var bytes = Encoding.Latin1.GetBytes(RemovePortableTemporaryFileTemplates(
                Encoding.Latin1.GetString(File.ReadAllBytes(archive))));
            Assert.All(forbidden, value => Assert.Equal(-1, bytes.AsSpan().IndexOf(value)));
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NetWasm.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"netwasm-archive-privacy-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string name, byte[] contents)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllBytes(path, contents);
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
