using System.Diagnostics;

namespace NetWasm.TestInfrastructure;

public sealed class TestAssets : IDisposable
{
    private TestAssets(
        string directory,
        string root,
        string coreLib,
        string library,
        string application)
    {
        Directory = directory;
        Root = root;
        CoreLib = coreLib;
        Library = library;
        Application = application;
    }

    public string Directory { get; }
    public string Root { get; }
    public string CoreLib { get; }
    public string Library { get; }
    public string Application { get; }

    public static TestAssets Create()
    {
        var root = FindRepositoryRoot();
        var directory = Path.Combine(
            Path.GetTempPath(),
            "netwasm-tests-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        var coreLib = Path.Combine(
            root,
            "src",
            "NetWasm.CoreLib",
            "bin",
            "Release",
            "net10.0",
            "NetWasm.CoreLib.dll");
        var library = Path.Combine(directory, "Fixture.Library.dll");
        var application = Path.Combine(directory, "Fixture.Application.dll");
        Compile(
            root,
            coreLib,
            library,
            Path.Combine(
                root,
                "tests",
                "end-to-end",
                "compiler-pipeline",
                "fixtures",
                "Library.cs"));
        Compile(
            root,
            coreLib,
            application,
            Path.Combine(
                root,
                "tests",
                "end-to-end",
                "compiler-pipeline",
                "fixtures",
                "Application.cs"),
            library);
        return new TestAssets(directory, root, coreLib, library, application);
    }

    public string CompileSource(string assemblyName, string source, params string[] references)
        => CompileSourceCore(assemblyName, source, allowUnsafe: false, references);

    public string CompileOptimizedSource(
        string assemblyName,
        string source,
        params string[] references)
    {
        var sourcePath = Path.Combine(Directory, assemblyName + ".cs");
        var output = Path.Combine(Directory, assemblyName + ".dll");
        File.WriteAllText(sourcePath, source);
        Compile(
            Root,
            CoreLib,
            output,
            [sourcePath],
            allowUnsafe: false,
            optimize: true,
            references: references);
        return output;
    }

    public string CompileSourceFiles(
        string assemblyName,
        IReadOnlyList<string> sources,
        params string[] references) =>
        CompileSourceFilesCore(assemblyName, sources, optimize: false, references: references);

    public string CompileOptimizedSourceFiles(
        string assemblyName,
        IReadOnlyList<string> sources,
        params string[] references) =>
        CompileSourceFilesCore(assemblyName, sources, optimize: true, references: references);

    public string CompileUnsafeSource(
        string assemblyName,
        string source,
        params string[] references) =>
        CompileSourceCore(assemblyName, source, allowUnsafe: true, references);

    public string CompileOptimizedUnsafeSource(
        string assemblyName,
        string source,
        params string[] references)
    {
        var sourcePath = Path.Combine(Directory, assemblyName + ".cs");
        var output = Path.Combine(Directory, assemblyName + ".dll");
        File.WriteAllText(sourcePath, source);
        Compile(
            Root,
            CoreLib,
            output,
            [sourcePath],
            allowUnsafe: true,
            optimize: true,
            references: references);
        return output;
    }

    public string CompileCoreLibVariant(string assemblyName, string additionalSource)
    {
        var sourcePath = Path.Combine(Directory, assemblyName + ".Additional.cs");
        var output = Path.Combine(Directory, assemblyName + ".dll");
        var coreLibDirectory = Path.Combine(Root, "src", "NetWasm.CoreLib");
        File.WriteAllText(sourcePath, additionalSource);
        var sources = System.IO.Directory
            .EnumerateFiles(coreLibDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .Append(sourcePath)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Compile(Root, null, output, sources, allowUnsafe: true);
        return output;
    }

    public string CompileSourceAgainstCoreLib(
        string assemblyName,
        string source,
        string coreLib)
    {
        var sourcePath = Path.Combine(Directory, assemblyName + ".cs");
        var output = Path.Combine(Directory, assemblyName + ".dll");
        File.WriteAllText(sourcePath, source);
        Compile(Root, coreLib, output, sourcePath, allowUnsafe: false);
        return output;
    }

    private string CompileSourceCore(
        string assemblyName,
        string source,
        bool allowUnsafe,
        params string[] references)
    {
        var sourcePath = Path.Combine(Directory, assemblyName + ".cs");
        var output = Path.Combine(Directory, assemblyName + ".dll");
        File.WriteAllText(sourcePath, source);
        Compile(Root, CoreLib, output, sourcePath, allowUnsafe, references);
        return output;
    }

    private string CompileSourceFilesCore(
        string assemblyName,
        IReadOnlyList<string> sources,
        bool optimize,
        params string[] references)
    {
        ArgumentNullException.ThrowIfNull(sources);
        if (sources.Count == 0)
        {
            throw new ArgumentException("At least one source file is required.", nameof(sources));
        }

        var sourcePaths = sources
            .Select((source, index) =>
            {
                var path = Path.Combine(Directory, $"{assemblyName}.{index}.cs");
                File.WriteAllText(path, source);
                return path;
            })
            .ToArray();
        var output = Path.Combine(Directory, assemblyName + ".dll");
        Compile(
            Root,
            CoreLib,
            output,
            sourcePaths,
            allowUnsafe: false,
            optimize: optimize,
            references: references);
        return output;
    }

    public void Dispose()
    {


        System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static void Compile(
        string root,
        string? coreLib,
        string output,
        string source,
        params string[] references)
        => Compile(root, coreLib, output, source, allowUnsafe: false, references);

    private static void Compile(
        string root,
        string? coreLib,
        string output,
        string source,
        bool allowUnsafe,
        params string[] references)
        => Compile(root, coreLib, output, [source], allowUnsafe, references);

    private static void Compile(
        string root,
        string? coreLib,
        string output,
        string[] sources,
        bool allowUnsafe,
        params string[] references)
        => Compile(root, coreLib, output, sources, allowUnsafe, optimize: false, references);

    private static void Compile(
        string root,
        string? coreLib,
        string output,
        string[] sources,
        bool allowUnsafe,
        bool optimize,
        params string[] references)
    {
        var sdkVersion = File.ReadAllText(Path.Combine(root, "global.json"))
            .Split("\"version\": \"", StringSplitOptions.None)[1]
            .Split('"')[0];
        var dotnet = Environment.ProcessPath
                     ?? throw new InvalidOperationException("dotnet host path is unavailable");
        var csc = Path.Combine(
            Path.GetDirectoryName(dotnet)!,
            "sdk",
            sdkVersion,
            "Roslyn",
            "bincore",
            "csc.dll");
        var start = new ProcessStartInfo(dotnet)
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        start.ArgumentList.Add(csc);
        string[] arguments =
        [
            "-nologo",
            "-noconfig",
            "-nostdlib",
            "-langversion:latest",
            "-define:NETWASM_REF_STRUCT_GENERICS;NETWASM_REGEX_STRING_CREATE;SYSTEM_TEXT_REGULAREXPRESSIONS",
            "-deterministic+",
            optimize ? "-optimize+" : "-optimize-",
            "-target:library",
            "-runtimemetadataversion:v4.0.30319",
            "-out:" + output,
        ];
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        if (coreLib is not null)
        {
            start.ArgumentList.Add("-reference:" + coreLib);
        }
        if (allowUnsafe)
        {
            start.ArgumentList.Add("-unsafe+");
        }
        foreach (var reference in references)
        {
            start.ArgumentList.Add("-reference:" + reference);
        }
        foreach (var source in sources)
        {
            start.ArgumentList.Add(source);
        }
        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("failed to start Roslyn");
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Roslyn failed: " + standardOutput + standardError);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null &&
               !File.Exists(Path.Combine(current.FullName, "global.json")))
        {
            current = current.Parent;
        }
        return current?.FullName
            ?? throw new InvalidOperationException("repository root was not found");
    }
}
