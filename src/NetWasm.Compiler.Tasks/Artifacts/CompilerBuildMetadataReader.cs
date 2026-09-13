using System.Collections.Immutable;
using System.Text.Json;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm;

namespace NetWasm.Compiler.Tasks.Artifacts;

internal sealed class CompilerBuildMetadataReader : ICompilerBuildMetadataReader
{
    public CompilerBuildMetadata Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        RequireObject(root, "compiler metadata");
        RequireExactProperties(root,
            "schemaVersion", "target", "runtimeFeatures", "functionImports");
        var schemaVersion = root.GetProperty("schemaVersion").GetInt32();
        if (schemaVersion != 1)
        {
            throw Invalid("compiler metadata schema version is unsupported");
        }
        var target = root.GetProperty("target").GetString();
        if (target is not ("wasm32" or "wasm64"))
        {
            throw Invalid("compiler metadata target is invalid");
        }

        var runtimeFeatures = ReadRuntimeFeatures(root.GetProperty("runtimeFeatures"));
        var functionImports = ReadFunctionImports(root.GetProperty("functionImports"));
        return new(schemaVersion, target, runtimeFeatures, functionImports);
    }

    private static ImmutableArray<string> ReadRuntimeFeatures(JsonElement value)
    {
        RequireArray(value, "compiler runtime features");
        var features = value.EnumerateArray().Select(item => item.GetString()).ToArray();
        if (features.Any(feature => feature != NetWasmRuntimeFeatureIds.LocalTime)
            || features.Distinct(StringComparer.Ordinal).Count() != features.Length
            || !features.SequenceEqual(features.Order(StringComparer.Ordinal)))
        {
            throw Invalid("compiler runtime features are invalid or non-canonical");
        }
        return [.. features!];
    }

    private static ImmutableArray<WasmFunctionImport> ReadFunctionImports(JsonElement value)
    {
        RequireArray(value, "compiler function imports");
        var imports = ImmutableArray.CreateBuilder<WasmFunctionImport>();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateArray())
        {
            RequireObject(item, "compiler function import");
            RequireExactProperties(item, "module", "name", "parameters", "result");
            var module = ReadNonEmptyString(item, "module");
            var name = ReadNonEmptyString(item, "name");
            if (!identities.Add(string.Join('\0', module, name)))
            {
                throw Invalid("compiler function imports contain a duplicate identity");
            }
            var parameters = ReadValueKinds(item.GetProperty("parameters"), allowVoid: false);
            var result = ReadValueKind(item.GetProperty("result"), allowVoid: true);
            imports.Add(new(module, name, new(parameters, result)));
        }
        return imports.ToImmutable();
    }

    private static ImmutableArray<CliValueKind> ReadValueKinds(
        JsonElement value,
        bool allowVoid)
    {
        RequireArray(value, "compiler function import parameters");
        return [.. value.EnumerateArray().Select(item => ReadValueKind(item, allowVoid))];
    }

    private static CliValueKind ReadValueKind(JsonElement value, bool allowVoid)
    {
        if (value.ValueKind != JsonValueKind.String
            || !Enum.TryParse<CliValueKind>(value.GetString(), ignoreCase: true, out var kind)
            || !Enum.IsDefined(kind)
            || !allowVoid && kind == CliValueKind.Void)
        {
            throw Invalid("compiler function import contains an invalid value kind");
        }
        return kind;
    }

    private static string ReadNonEmptyString(JsonElement value, string property)
    {
        var member = value.GetProperty(property);
        if (member.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(member.GetString()))
        {
            throw Invalid($"compiler metadata {property} is invalid");
        }
        return member.GetString()!;
    }

    private static void RequireExactProperties(JsonElement value, params string[] expected)
    {
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != expected.Length
            || actual.Except(expected, StringComparer.Ordinal).Any())
        {
            throw Invalid("compiler metadata shape is invalid");
        }
    }

    private static void RequireObject(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid($"{label} must be an object");
        }
    }

    private static void RequireArray(JsonElement value, string label)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw Invalid($"{label} must be an array");
        }
    }

    private static InvalidOperationException Invalid(string message) => new(message);
}
