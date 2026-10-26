using System.Collections.Immutable;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NetWasm.Hosting.Build.JavaScript;

/// <summary>A runtime module's file and every file it loads, itself included.</summary>
internal sealed record JavaScriptRuntimeModule(string File, ImmutableArray<string> Files);

/// <summary>
/// The <c>runtime.json</c> the runtime bundler writes beside its files, and
/// the runtime modules a generated entry imports.
/// </summary>
internal static partial class JavaScriptRuntimeManifest
{
    public static ImmutableDictionary<string, JavaScriptRuntimeModule> Read(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("schemaVersion", out var version) || version.ValueKind != JsonValueKind.Number
            || version.GetInt32() != 1
            || !root.TryGetProperty("modules", out var modules) || modules.ValueKind != JsonValueKind.Object)
        {
            throw Invalid();
        }
        var result = ImmutableDictionary.CreateBuilder<string, JavaScriptRuntimeModule>(StringComparer.Ordinal);
        foreach (var module in modules.EnumerateObject())
        {
            if (!ModuleName().IsMatch(module.Name)
                || module.Value.ValueKind != JsonValueKind.Object
                || !module.Value.TryGetProperty("file", out var file) || file.ValueKind != JsonValueKind.String
                || !module.Value.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                throw Invalid();
            }
            var names = files.EnumerateArray()
                .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid())
                .ToImmutableArray();
            if (names.Any(name => !FileName().IsMatch(name)) || !names.Contains(file.GetString()!))
            {
                throw Invalid();
            }
            result.Add(module.Name, new(file.GetString()!, names));
        }
        return result.ToImmutable();
    }

    /// <summary>The runtime modules a generated entry's import statements name.</summary>
    public static ImmutableArray<string> FindImports(string source) =>
        [.. RuntimeImport().Matches(source).Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    private static InvalidDataException Invalid() => new("The NetWasm JavaScript runtime manifest is invalid.");

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex ModuleName();

    [GeneratedRegex(@"^netwasm\.runtime\.[A-Za-z0-9._-]+\.mjs$")]
    private static partial Regex FileName();

    [GeneratedRegex("\"netwasm:runtime/([a-z][a-z0-9-]*)\"")]
    private static partial Regex RuntimeImport();
}
