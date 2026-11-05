using NetWasm.Hosting.Build.Environment;
using NetWasm.Hosting.Build.JavaScript;

namespace NetWasm.Hosting.Build.Tests;

public sealed class StampedJavaScriptBundlerTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("netwasm-bundle-stamp-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void LeavesABundleWhoseInputsAreUnchanged()
    {
        var module = Write("module.mjs", "export const a = 1;");
        var bundler = new ListingBundler(module);
        var stamped = new StampedJavaScriptBundler(bundler);
        var request = Request();

        stamped.Bundle(request);
        stamped.Bundle(request);
        Write("entry.mjs", "import './module.mjs';");
        stamped.Bundle(request);

        Assert.Equal(1, bundler.Bundles);
    }

    [Theory]
    [InlineData("module")]
    [InlineData("entry")]
    [InlineData("output")]
    [InlineData("request")]
    [InlineData("unlisted")]
    [InlineData("relative")]
    [InlineData("corrupt")]
    public void BundlesAgainWhenItCannotTellTheBundleIsCurrent(string change)
    {
        var module = Write("module.mjs", "export const a = 1;");
        var bundler = new ListingBundler(module);
        var stamped = new StampedJavaScriptBundler(bundler);
        var request = Request();
        stamped.Bundle(request);

        switch (change)
        {
            case "module": Write("module.mjs", "export const a = 2;"); break;
            case "entry": Write("entry.mjs", "import './module.mjs'; // changed"); break;
            case "output": File.Delete(request.OutputPath); break;
            case "request": request = request with { Minify = true }; break;
            case "unlisted": bundler.List = null; File.Delete(request.OutputPath + ".inputs.json"); break;
            case "relative": File.WriteAllText(request.OutputPath + ".inputs.json", "[\"module.mjs\"]"); break;
            case "corrupt": File.WriteAllText(request.OutputPath + ".inputs.json", "not json"); break;
        }
        stamped.Bundle(request);

        Assert.Equal(2, bundler.Bundles);
    }

    [Fact]
    public void BuildsTheRuntimeEveryTimeAndRequiresABundler()
    {
        var bundler = new ListingBundler(Write("module.mjs", ""));
        var stamped = new StampedJavaScriptBundler(bundler);

        stamped.BundleRuntime(new("node", "bundle.mjs", "facades", "out", Minify: false));

        Assert.Equal(1, bundler.Runtimes);
        Assert.Throws<ArgumentNullException>(() => new StampedJavaScriptBundler(null!));
        Assert.Throws<ArgumentNullException>(() => stamped.Bundle(null!));
    }

    [Fact]
    public void StampsValuesAndFileTrees()
    {
        var file = Write("a.txt", "a");
        var tree = Path.Combine(_root, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "nested"));
        File.WriteAllText(Path.Combine(tree, "nested", "b.txt"), "b");
        var before = FileTreeStamp.Compute(["value"], [file, tree, Path.Combine(_root, "missing"), null]);

        Assert.Equal(before, FileTreeStamp.Compute(["value"], [file, tree, Path.Combine(_root, "missing"), null]));
        Assert.NotEqual(before, FileTreeStamp.Compute(["other"], [file, tree, Path.Combine(_root, "missing"), null]));
        File.WriteAllText(Path.Combine(tree, "nested", "c.txt"), "c");
        Assert.NotEqual(before, FileTreeStamp.Compute(["value"], [file, tree, Path.Combine(_root, "missing"), null]));
        Assert.Throws<ArgumentNullException>(() => FileTreeStamp.Compute(null!, []));
        Assert.Throws<ArgumentNullException>(() => FileTreeStamp.Compute([], null!));
    }

    private JavaScriptBundleRequest Request()
    {
        Write("entry.mjs", "import './module.mjs';");
        return new(
            Path.Combine(_root, "node"),
            Path.Combine(_root, "bundler", "bundle.mjs"),
            Path.Combine(_root, "entry.mjs"),
            Path.Combine(_root, "out", "page.mjs"),
            "browser",
            Minify: false);
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    // Writes the bundle and, as the Node bundler does, the list of files it read.
    private sealed class ListingBundler(string module) : IJavaScriptBundler
    {
        public string[]? List { get; set; } = [module];

        public int Bundles { get; private set; }

        public int Runtimes { get; private set; }

        public void Bundle(JavaScriptBundleRequest request)
        {
            Bundles++;
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            File.WriteAllText(request.OutputPath, "bundle");
            if (List is not null)
            {
                File.WriteAllText(request.OutputPath + ".inputs.json",
                    "[" + string.Join(",", List.Select(path => "\"" + path.Replace("\\", "\\\\") + "\"")) + "]");
            }
        }

        public void BundleRuntime(JavaScriptRuntimeBundleRequest request) => Runtimes++;
    }
}
