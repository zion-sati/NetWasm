using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.ComponentModel.Catalogs;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class RawBindingManifestReader : IRawBindingManifestReader
{
    public RawBindingManifest Read(string path, string target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        RequireKind(root, JsonValueKind.Object, "raw binding manifest");
        RequireExactProperties(root, "schemaVersion", "target", "requiredImports");
        if (root.GetProperty("schemaVersion").GetInt32() != 1)
        {
            throw Invalid("raw binding manifest schema version is unsupported");
        }
        var manifestTarget = root.GetProperty("target").GetString();
        if (manifestTarget is not ("wasm32" or "wasm64")
            || !string.Equals(manifestTarget, target, StringComparison.Ordinal))
        {
            throw Invalid("raw binding manifest target is invalid");
        }
        var importsValue = root.GetProperty("requiredImports");
        RequireKind(importsValue, JsonValueKind.Array, "raw binding imports");
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var imports = ImmutableArray.CreateBuilder<WitInterfaceFunction>();
        foreach (var value in importsValue.EnumerateArray())
        {
            RequireKind(value, JsonValueKind.Object, "raw binding import");
            RequireExactProperties(value, "interface", "name", "parameters", "results");
            var @interface = ReadNonEmptyString(value, "interface");
            var name = ReadNonEmptyString(value, "name");
            if (!identities.Add(string.Join('\0', @interface, name)))
            {
                throw Invalid("raw binding imports contain a duplicate identity");
            }
            imports.Add(new(
                @interface,
                name,
                ReadStrings(value.GetProperty("parameters"), "raw binding parameters"),
                ReadStrings(value.GetProperty("results"), "raw binding results")));
        }
        var requiredImports = imports.ToImmutable();
        if (!requiredImports.SequenceEqual(requiredImports
                .OrderBy(item => item.Interface, StringComparer.Ordinal)
                .ThenBy(item => item.Name, StringComparer.Ordinal)))
        {
            throw Invalid("raw binding imports are not canonical");
        }
        return new(1, manifestTarget, requiredImports);
    }

    private static ImmutableArray<string> ReadStrings(JsonElement value, string label)
    {
        RequireKind(value, JsonValueKind.Array, label);
        var values = value.EnumerateArray().Select(item => item.GetString()).ToArray();
        if (values.Any(string.IsNullOrWhiteSpace)) throw Invalid($"{label} are invalid");
        return [.. values!];
    }

    private static string ReadNonEmptyString(JsonElement value, string property)
    {
        var result = value.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(result)
            ? throw Invalid($"raw binding {property} is invalid")
            : result;
    }

    private static void RequireExactProperties(JsonElement value, params string[] expected)
    {
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != expected.Length || actual.Except(expected, StringComparer.Ordinal).Any())
        {
            throw Invalid("raw binding manifest shape is invalid");
        }
    }

    private static void RequireKind(JsonElement value, JsonValueKind kind, string label)
    {
        if (value.ValueKind != kind) throw Invalid($"{label} has an invalid shape");
    }

    private static InvalidDataException Invalid(string message) => new(message);
}
