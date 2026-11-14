using System.Text.Json;
using System.Xml.Linq;

namespace NetWasm.Sdk.Pack.Tests;

public sealed class TemplateContractTests
{
    [Theory]
    [InlineData("NetWasm.App")]
    [InlineData("NetWasm.Library")]
    [InlineData("NetWasm.Serverless")]
    public void NamedProjectsPreferTheirOwnDirectory(string templateName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "NetWasm.Templates", "content", templateName,
            ".template.config", "template.json")));

        Assert.True(document.RootElement.GetProperty("preferNameDirectory").GetBoolean());
    }

    // An empty source folder is not packed, and dotnet new then refuses the
    // whole template (MV012).
    [Theory]
    [InlineData("NetWasm.App")]
    [InlineData("NetWasm.Library")]
    [InlineData("NetWasm.Serverless")]
    public void EachTemplateSourceHoldsFiles(string templateName)
    {
        var template = Path.Combine(FindRepositoryRoot(), "src", "NetWasm.Templates", "content", templateName);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            template, ".template.config", "template.json")));
        var sources = document.RootElement.TryGetProperty("sources", out var declared)
            ? declared.EnumerateArray().Select(source => source.GetProperty("source").GetString()!).ToArray()
            : ["."];

        foreach (var source in sources)
        {
            var directory = Path.Combine(template, source);
            Assert.True(Directory.Exists(directory) &&
                Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                    .Any(file => !file.Contains(".template.config", StringComparison.Ordinal)),
                $"{templateName}'s source '{source}' holds no files.");
        }
    }

    [Theory]
    [InlineData("src/NetWasm.Compiler.Tasks/NetWasm.Compiler.Tasks.csproj", "CanonicalizeNetWasmCompilerTasksPackage")]
    [InlineData("src/NetWasm.Sdk/NetWasm.Sdk.csproj", "CanonicalizeNetWasmSdkPackage")]
    [InlineData("src/NetWasm.Templates/NetWasm.Templates.csproj", "CanonicalizeNetWasmTemplatesPackage")]
    [InlineData("eng/NetWasm.DeterministicPackageArchive.targets", "CanonicalizeNetWasmPackageArchive")]
    public void PackageTargetsCanonicalizeTheirArchives(
        string projectPath,
        string targetName)
    {
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), projectPath));

        Assert.Contains(project.Descendants("UsingTask"), task =>
            string.Equals((string?)task.Attribute("TaskName"),
                "NetWasm.Sdk.Pack.DeterministicPackageArchiveTask",
                StringComparison.Ordinal));
        var target = Assert.Single(project.Descendants("Target"), candidate =>
            string.Equals((string?)candidate.Attribute("Name"), targetName,
                StringComparison.Ordinal));
        Assert.Equal("GenerateNuspec", (string?)target.Attribute("AfterTargets"));
        Assert.Single(target.Descendants("DeterministicPackageArchiveTask"));
    }

    [Theory]
    [InlineData("src/NetWasm.Runtime.Pack/NetWasm.Runtime.Pack.csproj")]
    [InlineData("src/NetWasm.Runtime.Wasm32/NetWasm.Runtime.Wasm32.csproj")]
    [InlineData("src/NetWasm.Runtime.Wasm64/NetWasm.Runtime.Wasm64.csproj")]
    public void RuntimePackagesImportSharedArchiveCanonicalization(string projectPath)
    {
        var project = XDocument.Load(Path.Combine(FindRepositoryRoot(), projectPath));

        Assert.Contains(project.Descendants("Import"), import =>
            string.Equals(
                (string?)import.Attribute("Project"),
                "../../eng/NetWasm.DeterministicPackageArchive.targets",
                StringComparison.Ordinal));
    }

    // A generated project builds with whichever SDK the user's dotnet selects.
    [Theory]
    [InlineData("NetWasm.App")]
    [InlineData("NetWasm.Library")]
    [InlineData("NetWasm.Serverless")]
    public void GeneratedProjectUsesTheUsersSdk(string templateName)
    {
        var template = Path.Combine(FindRepositoryRoot(), "src", "NetWasm.Templates", "content", templateName);

        Assert.Empty(Directory.EnumerateFiles(template, "global.json", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("NetWasm.App/console/NetWasmProject.csproj")]
    [InlineData("NetWasm.App/host-page/NetWasmProject.csproj")]
    [InlineData("NetWasm.App/host-worker/NetWasmProject.csproj")]
    [InlineData("NetWasm.App/wit/NetWasmProject.csproj")]
    [InlineData("NetWasm.Library/NetWasmLibrary.csproj")]
    [InlineData("NetWasm.Serverless/NetWasmService.csproj")]
    public void GeneratedProjectNamesTheSdkVersionItWasCreatedWith(string projectPath)
    {
        var repositoryRoot = FindRepositoryRoot();
        var declaredSdkVersion = XDocument
            .Load(Path.Combine(repositoryRoot, "src", "NetWasm.Sdk", "NetWasm.Sdk.csproj"))
            .Descendants("Version")
            .Single()
            .Value;

        var project = XDocument.Load(Path.Combine(repositoryRoot, "src", "NetWasm.Templates", "content", projectPath));

        Assert.Equal($"NetWasm.Sdk/{declaredSdkVersion}", (string?)project.Root!.Attribute("Sdk"));
    }

    // The serverless template is an HTTP service; System.Net.Http and the wasi:http service bridge come with the SDK
    // and NetWasm.App, so it references no package.
    [Fact]
    public void ServerlessTemplateIsAnHttpServiceWithNoPackageReference()
    {
        var repositoryRoot = FindRepositoryRoot();
        var template = Path.Combine(repositoryRoot, "src", "NetWasm.Templates", "content", "NetWasm.Serverless");
        var project = XDocument.Load(Path.Combine(template, "NetWasmService.csproj"));

        Assert.Equal("Component", project.Descendants("OutputType").Single().Value);
        Assert.Equal("service", project.Descendants("NetWasmWorld").Single().Value);
        Assert.Empty(project.Descendants("PackageReference"));
        Assert.Contains(
            "public static async partial Task<HttpResponseMessage> Handle(HttpRequestMessage request)",
            File.ReadAllText(Path.Combine(template, "ServiceExports.cs")),
            StringComparison.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            template, ".template.config", "template.json")));
        Assert.Equal("netwasm-serverless", document.RootElement.GetProperty("shortName").GetString());
    }

    [Fact]
    public void ApplicationTemplateOffersJavaScriptModuleHostsAndAWitWorker()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "NetWasm.Templates", "content", "NetWasm.App",
            ".template.config", "template.json")));

        var root = document.RootElement;
        Assert.Equal("NetWasmProject", root.GetProperty("sourceName").GetString());
        var symbols = root.GetProperty("symbols");
        var host = symbols.GetProperty("host");
        Assert.Equal("none", host.GetProperty("defaultValue").GetString());
        // A bare --host is not applied by the template engine, so the host is always named.
        Assert.False(host.TryGetProperty("defaultIfOptionWithoutValue", out _));
        Assert.Equal(
            ["none", "page", "worker"],
            host.GetProperty("choices").EnumerateArray()
                .Select(choice => choice.GetProperty("choice").GetString()));
        var worker = symbols.GetProperty("worker");
        Assert.Equal("none", worker.GetProperty("defaultValue").GetString());
        Assert.Equal(
            ["none", "wit"],
            worker.GetProperty("choices").EnumerateArray()
                .Select(choice => choice.GetProperty("choice").GetString()));
        Assert.Equal(
            ["console", "worker-common", "wit", "host-page", "host-worker"],
            root.GetProperty("sources").EnumerateArray()
                .Select(source => source.GetProperty("source").GetString()));
    }

    [Fact]
    public void PageTemplateOpensCallsAndClosesItsModule()
    {
        var templateRoot = Path.Combine(
            FindRepositoryRoot(), "src", "NetWasm.Templates", "content", "NetWasm.App", "host-page");
        var project = File.ReadAllText(Path.Combine(templateRoot, "NetWasmProject.csproj"));
        var program = File.ReadAllText(Path.Combine(templateRoot, "Program.cs"));
        var app = File.ReadAllLines(Path.Combine(templateRoot, "app.mjs"))
            .Where(line => line.Length != 0).ToArray();

        Assert.Contains("<OutputType>JsModule</OutputType>", project, StringComparison.Ordinal);
        Assert.Contains("<NetWasmJavaScriptHosts>page</NetWasmJavaScriptHosts>", project, StringComparison.Ordinal);
        Assert.Contains("<NetWasmJavaScriptModule Include=\"app.host.mjs\" Module=\"app.host\" />", project,
            StringComparison.Ordinal);
        Assert.Contains("[JSImport(\"show\", \"app.host\")]", program, StringComparison.Ordinal);
        Assert.Contains("[JSExport(\"greet\")]", program, StringComparison.Ordinal);
        Assert.Equal(
            [
                "import { open } from \"./browser/NetWasmProject.mjs\";",
                "const app = await open();",
                "app.greet(\"browser\");",
                "await app.close();",
            ],
            app);
    }

    [Fact]
    public void WorkerHostTemplateDeclaresBodylessJavaScriptImports()
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "NetWasm.Templates", "content", "NetWasm.App",
            "host-worker", "Program.cs"));

        Assert.Contains("private static extern void Report", source, StringComparison.Ordinal);
        Assert.DoesNotContain("partial void Report", source, StringComparison.Ordinal);
        Assert.Contains("completed < total", source, StringComparison.Ordinal);
        Assert.DoesNotContain("completed <= total", source, StringComparison.Ordinal);
    }

    [Fact]
    public void WorkerTemplateCallersUseTheirPublishedOperationNames()
    {
        var templateRoot = Path.Combine(
            FindRepositoryRoot(), "src", "NetWasm.Templates", "content", "NetWasm.App");
        var wit = File.ReadAllText(Path.Combine(templateRoot, "wit", "app.mjs"));
        var jsExport = File.ReadAllText(Path.Combine(templateRoot, "host-worker", "app.mjs"));

        Assert.Contains(
            "netwasm:worker-template/work@1.0.0/run", wit, StringComparison.Ordinal);
        Assert.Contains("worker.run(5)", jsExport, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "src/NetWasm.Sdk/NetWasm.Sdk.csproj")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
