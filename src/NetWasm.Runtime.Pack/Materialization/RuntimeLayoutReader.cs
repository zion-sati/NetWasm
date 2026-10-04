using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetWasm.Runtime.Pack.Materialization;

internal sealed class RuntimeLayoutReader : IRuntimeLayoutReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public RuntimeLayout Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException("The NetWasm runtime layout evidence is missing.");
        }

        RuntimeLayout layout;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            ValidateContract(root);
            // ValidateContract already requires an object. The built-in record
            // converter cannot return null for this non-null JSON object.
            layout = root.Deserialize<RuntimeLayout>(JsonOptions)!;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("The NetWasm runtime layout evidence is malformed.", exception);
        }

        if (layout.SchemaVersion is not (2 or 3 or 4) ||
            layout.Target is not ("wasm32" or "wasm64") ||
            layout.ApplicationStaticDataEnd < 0 ||
            layout.SchemaVersion == 2 && !layout.NativeImports.IsEmpty ||
            layout.SchemaVersion < 4 && layout.NativeCallbackSupport is not null ||
            layout.SchemaVersion == 4 && layout.NativeCallbackSupport is null ||
            layout.NativeImports.Any(import =>
                string.IsNullOrWhiteSpace(import.LibraryName) ||
                string.IsNullOrWhiteSpace(import.EntryPoint)) ||
            !ValidCallbackSupport(layout.NativeCallbackSupport))
        {
            throw new InvalidOperationException("The NetWasm runtime layout evidence is invalid.");
        }

        return layout;
    }

    private static void ValidateContract(JsonElement root)
    {
        var fields = ReadFields(root,
        [
            "schemaVersion",
            "target",
            "applicationStaticDataEnd",
            "managedExecutableEntryPoint",
            "nativeImports",
            "nativeCallbackSupport",
        ]);
        if (!fields.TryGetValue("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.Number ||
            !schema.TryGetInt32(out var version) ||
            !fields.ContainsKey("target") || !fields.ContainsKey("applicationStaticDataEnd"))
            throw InvalidContract();
        if (!fields.TryGetValue("nativeImports", out var imports))
        {
            if (version != 2 || fields.ContainsKey("nativeCallbackSupport"))
                throw InvalidContract();
            return;
        }
        if (imports.ValueKind != JsonValueKind.Array)
            throw InvalidContract();
        foreach (var import in imports.EnumerateArray())
        {
            var nativeFields = ReadFields(import, ["libraryName", "entryPoint", "parameters", "returnType"]);
            if (nativeFields.Count != 4 || nativeFields["parameters"].ValueKind != JsonValueKind.Array)
                throw InvalidContract();
        }
        if (version == 4)
        {
            if (!fields.TryGetValue("nativeCallbackSupport", out var support) ||
                support.ValueKind != JsonValueKind.Object)
            {
                throw InvalidContract();
            }
            ValidateCallbackContract(support);
        }
        else if (fields.ContainsKey("nativeCallbackSupport"))
        {
            throw InvalidContract();
        }
    }

    private static void ValidateCallbackContract(JsonElement support)
    {
        var fields = ReadFields(support,
        [
            "fileName",
            "sha256",
            "callbacks",
            "temporaryApplicationExports",
            "temporaryRuntimeExports",
        ]);
        if (fields.Count != 5 ||
            fields["fileName"].ValueKind != JsonValueKind.String ||
            fields["sha256"].ValueKind != JsonValueKind.String ||
            fields["callbacks"].ValueKind != JsonValueKind.Array ||
            fields["temporaryApplicationExports"].ValueKind != JsonValueKind.Array ||
            fields["temporaryRuntimeExports"].ValueKind != JsonValueKind.Array)
        {
            throw InvalidContract();
        }
        foreach (var callback in fields["callbacks"].EnumerateArray())
        {
            var callbackFields = ReadFields(callback,
            [
                "nativeSymbol",
                "runtimeImportSymbol",
                "applicationExportName",
                "runtimeGetterExportName",
                "parameters",
                "returnType",
            ]);
            if (callbackFields.Count != 6 ||
                callbackFields["parameters"].ValueKind != JsonValueKind.Array)
            {
                throw InvalidContract();
            }
        }
    }

    private static bool ValidCallbackSupport(RuntimeNativeCallbackSupport? support)
    {
        if (support is null)
        {
            return true;
        }
        return !string.IsNullOrWhiteSpace(support.FileName) &&
            Path.GetFileName(support.FileName) == support.FileName &&
            support.FileName.EndsWith(".o", StringComparison.Ordinal) &&
            ValidDigest(support.Sha256) &&
            !support.Callbacks.IsDefaultOrEmpty &&
            !support.TemporaryApplicationExports.IsDefault &&
            !support.TemporaryRuntimeExports.IsDefault &&
            support.Callbacks.All(callback =>
                callback is not null &&
                ValidName(callback.NativeSymbol) &&
                ValidName(callback.RuntimeImportSymbol) &&
                ValidName(callback.ApplicationExportName) &&
                (callback.RuntimeGetterExportName is null ||
                 ValidName(callback.RuntimeGetterExportName)) &&
                !callback.Parameters.IsDefault) &&
            support.TemporaryApplicationExports.SequenceEqual(
                support.Callbacks
                    .Where(callback => callback.NativeSymbol !=
                        callback.ApplicationExportName)
                    .Select(callback => callback.ApplicationExportName),
                StringComparer.Ordinal) &&
            support.TemporaryRuntimeExports.SequenceEqual(
                support.Callbacks
                    .Where(callback => callback.RuntimeGetterExportName is not null)
                    .Select(callback => callback.RuntimeGetterExportName!),
                StringComparer.Ordinal) &&
            HasUniqueCallbackSymbols(support) &&
            support.TemporaryApplicationExports.Distinct(StringComparer.Ordinal).Count() ==
                support.TemporaryApplicationExports.Length &&
            support.TemporaryRuntimeExports.Distinct(StringComparer.Ordinal).Count() ==
                support.TemporaryRuntimeExports.Length;
    }

    private static bool ValidDigest(string? value) =>
        value is { Length: 64 } && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private static bool ValidName(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.IndexOfAny(['\0', '\r', '\n']) < 0;

    private static bool HasUniqueCallbackSymbols(
        RuntimeNativeCallbackSupport support)
    {
        var applicationExports = support.Callbacks
            .Select(callback => callback.ApplicationExportName)
            .ToArray();
        var linkerSymbols = support.Callbacks
            .Select(callback => callback.RuntimeImportSymbol)
            .Concat(support.Callbacks
                .Where(callback => !string.Equals(
                    callback.NativeSymbol,
                    callback.RuntimeImportSymbol,
                    StringComparison.Ordinal))
                .Select(callback => callback.NativeSymbol))
            .Concat(support.Callbacks
                .Where(callback => callback.RuntimeGetterExportName is not null)
                .Select(callback => callback.RuntimeGetterExportName!))
            .ToArray();
        return applicationExports.Distinct(StringComparer.Ordinal).Count() ==
                applicationExports.Length &&
            linkerSymbols.Distinct(StringComparer.Ordinal).Count() ==
                linkerSymbols.Length;
    }

    private static Dictionary<string, JsonElement> ReadFields(JsonElement value, string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw InvalidContract();
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.OrdinalIgnoreCase) ||
                !fields.TryAdd(property.Name, property.Value))
                throw InvalidContract();
        }
        return fields;
    }

    private static InvalidOperationException InvalidContract() =>
        new("The NetWasm runtime layout evidence is invalid.");
}
