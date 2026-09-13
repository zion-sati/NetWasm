using System.Collections.Immutable;

namespace NetWasm.Hosting.Build.JavaScript;

public sealed record ComponentTranspileRequest(
    string NodePath,
    string JcoPath,
    string ComponentPath,
    string OutputDirectory,
    string BaseName);

public sealed record ComponentTranspileResult(
    string JavaScriptPath,
    ImmutableArray<string> CoreModulePaths);

public interface IComponentTranspiler
{
    ComponentTranspileResult Transpile(ComponentTranspileRequest request);
}

public sealed class ComponentTranspiler(IJavaScriptProcessRunner process) : IComponentTranspiler
{
    private readonly IJavaScriptProcessRunner _process = process ??
        throw new ArgumentNullException(nameof(process));

    public ComponentTranspiler()
        : this(new JavaScriptProcessRunner())
    {
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
            _process.Run(new(
                request.NodePath,
                request.JcoPath,
                [
                    "transpile",
                    request.ComponentPath,
                    "--out-dir",
                    temporary,
                    "--name",
                    request.BaseName,
                    "--instantiation",
                    "async",
                    "--strict",
                    "--bindgen-enable-wasm-exnref",
                    "--no-wasi-shim",
                    "--no-typescript",
                    "--quiet",
                ],
                "component transpiler"));
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
            var runtimeFiles = cores.Append(javaScript).ToHashSet(StringComparer.Ordinal);
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

            if (Directory.Exists(request.OutputDirectory))
            {
                Directory.Delete(request.OutputDirectory, recursive: true);
            }
            Directory.Move(temporary, request.OutputDirectory);
            return new(
                Path.Combine(request.OutputDirectory, request.BaseName + ".js"),
                [.. cores.Select(path => Path.Combine(
                    request.OutputDirectory,
                    Path.GetFileName(path)))]);
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
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
