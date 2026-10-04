using System.Collections.Immutable;
using System.Text.Json;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record ComponentTranspileRequest(
    string NodePath,
    string JcoPath,
    string ComponentPath,
    string OutputDirectory,
    string BaseName);

public sealed record ComponentTranspileResult(
    string JavaScriptPath,
    ImmutableArray<string> CoreModulePaths,
    ImmutableArray<JcoComponentRootExport> RootExports);

public sealed record JcoComponentRootExport(string Name, string Kind);

public interface IComponentTranspiler
{
    ComponentTranspileResult Transpile(ComponentTranspileRequest request);
}

public sealed class ComponentTranspiler : IComponentTranspiler
{
    private const string MetadataFileName = ".netwasm-jco-exports.json";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
    private readonly IJavaScriptProcessRunner _process;
    private readonly string _runnerPath;

    public ComponentTranspiler()
        : this(
            new JavaScriptProcessRunner(),
            Path.Combine(
                Path.GetDirectoryName(typeof(ComponentTranspiler).Assembly.Location)!,
                "run-jco-transpile.mjs"))
    {
    }

    internal ComponentTranspiler(
        IJavaScriptProcessRunner process,
        string? runnerPath = null)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _runnerPath = runnerPath ?? Path.Combine(
            Path.GetDirectoryName(typeof(ComponentTranspiler).Assembly.Location)!,
            "run-jco-transpile.mjs");
    }

    public ComponentTranspileResult Transpile(ComponentTranspileRequest request)
    {
        Validate(request);
        var parent = Path.GetDirectoryName(request.OutputDirectory)!;
        Directory.CreateDirectory(parent);
        var temporary = Path.Combine(
            parent,
            $".{Path.GetFileName(request.OutputDirectory)}.{Guid.NewGuid():N}.tmp");
        Directory.CreateDirectory(temporary);
        try
        {
            var metadataPath = Path.Combine(temporary, MetadataFileName);
            _process.Run(new(
                request.NodePath,
                Path.GetFullPath(_runnerPath),
                [
                    Path.Combine(Path.GetDirectoryName(request.JcoPath)!, "api.js"),
                    request.ComponentPath,
                    temporary,
                    request.BaseName,
                    metadataPath,
                ],
                "component transpiler"));
            var rootExports = ReadMetadata(metadataPath);
            var files = Directory.GetFiles(temporary, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var javaScript = Path.Combine(temporary, request.BaseName + ".js");
            var cores = files.Where(path =>
                    string.Equals(Path.GetDirectoryName(path), temporary, StringComparison.Ordinal)
                    &&
                    Path.GetFileName(path).StartsWith(
                        request.BaseName + ".core",
                        StringComparison.Ordinal)
                    && string.Equals(Path.GetExtension(path), ".wasm", StringComparison.Ordinal))
                .ToArray();
            var typeDeclarations = files.Where(path =>
                    path.EndsWith(".d.ts", StringComparison.Ordinal))
                .ToArray();
            var runtimeFiles = cores.Append(javaScript)
                .Append(metadataPath)
                .ToHashSet(StringComparer.Ordinal);
            var unexpectedFiles = files.Except(runtimeFiles, StringComparer.Ordinal)
                .Except(typeDeclarations, StringComparer.Ordinal)
                .ToArray();
            var directories = Directory.GetDirectories(
                    temporary,
                    "*",
                    SearchOption.AllDirectories)
                .OrderDescending(StringComparer.Ordinal)
                .ToArray();
            var declarationDirectoryPrefix = Path.DirectorySeparatorChar.ToString();
            var unexpectedDirectories = directories.Where(directory =>
                    !typeDeclarations.Any(declaration => declaration.StartsWith(
                        Path.TrimEndingDirectorySeparator(directory) + declarationDirectoryPrefix,
                        StringComparison.Ordinal)))
                .ToArray();
            if (!File.Exists(javaScript) || cores.Length == 0
                || !string.Equals(
                    Path.GetDirectoryName(javaScript),
                    temporary,
                    StringComparison.Ordinal)
                || unexpectedFiles.Length != 0
                || unexpectedDirectories.Length != 0)
            {
                throw new InvalidOperationException(
                    "The NetWasm component transpiler produced an unexpected artifact closure.");
            }

            foreach (var typeDeclaration in typeDeclarations)
            {
                File.Delete(typeDeclaration);
            }
            foreach (var directory in directories)
            {
                Directory.Delete(directory);
            }
            File.Delete(metadataPath);

            if (Directory.Exists(request.OutputDirectory))
            {
                Directory.Delete(request.OutputDirectory, recursive: true);
            }
            Directory.Move(temporary, request.OutputDirectory);
            return new(
                Path.Combine(request.OutputDirectory, request.BaseName + ".js"),
                [.. cores.Select(path => Path.Combine(
                    request.OutputDirectory,
                    Path.GetFileName(path)))],
                rootExports);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static ImmutableArray<JcoComponentRootExport> ReadMetadata(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                "The NetWasm component transpiler produced no root export metadata.");
        }
        JcoTranspileMetadata? metadata;
        try
        {
            metadata = JsonSerializer.Deserialize<JcoTranspileMetadata>(
                File.ReadAllBytes(path),
                JsonOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "The NetWasm component transpiler produced invalid root export metadata.",
                exception);
        }
        if (metadata is null || metadata.SchemaVersion != 1
            || metadata.Exports.IsDefault
            || metadata.Exports.Any(value =>
                value is null
                || string.IsNullOrWhiteSpace(value.Name)
                || value.Kind is not ("function" or "instance"))
            || metadata.Exports.Select(value => value!.Name)
                .Distinct(StringComparer.Ordinal).Count() != metadata.Exports.Length)
        {
            throw new InvalidOperationException(
                "The NetWasm component transpiler produced invalid root export metadata.");
        }
        return [.. metadata.Exports.Select(value => value!)];
    }

    private sealed record JcoTranspileMetadata
    {
        public int SchemaVersion { get; init; }
        public ImmutableArray<JcoComponentRootExport?> Exports { get; init; }
    }

    private static void Validate(ComponentTranspileRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        foreach (var path in new[]
                 {
                     request.NodePath,
                     request.JcoPath,
                     request.ComponentPath,
                     request.OutputDirectory,
                 })
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            if (!Path.IsPathFullyQualified(path))
            {
                throw new ArgumentException("Component transpile paths must be absolute.", nameof(request));
            }
        }
        if (Path.GetPathRoot(request.OutputDirectory) ==
                Path.TrimEndingDirectorySeparator(request.OutputDirectory)
            || Path.GetDirectoryName(request.OutputDirectory) is null)
        {
            throw new ArgumentException(
                "Component transpile output must be a bounded directory.",
                nameof(request));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(request.BaseName);
        if (request.BaseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || request.BaseName.Contains(Path.DirectorySeparatorChar)
            || request.BaseName.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException(
                "Component transpile base name must be a file name.",
                nameof(request));
        }
    }
}
