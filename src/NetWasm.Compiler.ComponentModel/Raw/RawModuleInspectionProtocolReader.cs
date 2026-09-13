using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text.Json;

namespace NetWasm.Compiler.ComponentModel.Raw;

public interface IRawModuleInspectionProtocolReader
{
    ImmutableArray<RawCoreFunctionImportSignature> Read(ToolResult result);
}

public sealed class RawModuleInspectionProtocolReader : IRawModuleInspectionProtocolReader
{
    private const string SchemaVersion = "1";
    private const string ProtocolKind = "raw-module-import-signatures";

    private static readonly ImmutableHashSet<string> ErrorCodes =
        ImmutableHashSet.Create(StringComparer.Ordinal,
            "invalid-arguments",
            "invalid-bytes",
            "invalid-core-module",
            "unsupported-import-kind",
            "unsupported-import-signature",
            "duplicate-import",
            "decoder-failure",
            "inconsistent-import-inventory",
            "inspection-command-failure");

    public ImmutableArray<RawCoreFunctionImportSignature> Read(ToolResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var payload = ReadPayload(result.StandardOutput);
        try
        {
            using var document = JsonDocument.Parse(payload);
            var properties = ReadProperties(document.RootElement);
            RequireString(properties, "schemaVersion", SchemaVersion);
            RequireString(properties, "kind", ProtocolKind);
            return result.ExitCode == 0
                ? ReadSuccess(properties)
                : ReadFailure(properties);
        }
        catch (JsonException)
        {
            throw InvalidProtocol();
        }
    }

    private static string ReadPayload(string output)
    {
        if (string.IsNullOrEmpty(output)
            || output.Length < 3
            || output[^1] != '\n'
            || output[^2] == '\r')
        {
            throw InvalidProtocol();
        }
        var payload = output[..^1];
        if (payload[0] != '{' || payload[^1] != '}'
            || payload.Contains('\r', StringComparison.Ordinal)
            || payload.Contains('\n', StringComparison.Ordinal))
        {
            throw InvalidProtocol();
        }
        return payload;
    }

    private static Dictionary<string, JsonElement> ReadProperties(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw InvalidProtocol();
        }
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in root.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw InvalidProtocol();
            }
        }
        return properties;
    }

    private static ImmutableArray<RawCoreFunctionImportSignature> ReadSuccess(
        Dictionary<string, JsonElement> properties)
    {
        if (properties.Count != 3
            || !properties.TryGetValue("imports", out var imports)
            || imports.ValueKind != JsonValueKind.Array)
        {
            throw InvalidProtocol();
        }
        var result = ImmutableArray.CreateBuilder<RawCoreFunctionImportSignature>();
        var identities = new HashSet<RawCanonicalImportIdentity>();
        foreach (var element in imports.EnumerateArray())
        {
            var import = ReadImport(element);
            if (!identities.Add(import.Identity))
            {
                throw InvalidProtocol();
            }
            result.Add(import);
        }
        return result.ToImmutable();
    }

    private static ImmutableArray<RawCoreFunctionImportSignature> ReadFailure(
        Dictionary<string, JsonElement> properties)
    {
        if (properties.Count != 3
            || !properties.TryGetValue("errorCode", out var errorCode)
            || errorCode.ValueKind != JsonValueKind.String)
        {
            throw InvalidProtocol();
        }
        var code = errorCode.GetString()!;
        if (!ErrorCodes.Contains(code))
        {
            throw InvalidProtocol();
        }
        throw ComponentException.Tool($"raw module inspection failed with '{code}'");
    }

    private static RawCoreFunctionImportSignature ReadImport(JsonElement element)
    {
        var properties = ReadProperties(element);
        if (properties.Count != 4)
        {
            throw InvalidProtocol();
        }
        return new(
            new(ReadString(properties, "module"), ReadString(properties, "name")),
            ReadTypes(properties, "parameters"),
            ReadTypes(properties, "results"));
    }

    private static ImmutableArray<RawCoreValueType> ReadTypes(
        Dictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var values)
            || values.ValueKind != JsonValueKind.Array)
        {
            throw InvalidProtocol();
        }
        var result = ImmutableArray.CreateBuilder<RawCoreValueType>();
        foreach (var value in values.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String)
            {
                throw InvalidProtocol();
            }
            result.Add(value.GetString() switch
            {
                "i32" => RawCoreValueType.I32,
                "i64" => RawCoreValueType.I64,
                "f32" => RawCoreValueType.F32,
                "f64" => RawCoreValueType.F64,
                _ => throw InvalidProtocol(),
            });
        }
        return result.ToImmutable();
    }

    private static string ReadString(
        Dictionary<string, JsonElement> properties,
        string name)
    {
        if (!properties.TryGetValue(name, out var value)
            || value.ValueKind != JsonValueKind.String)
        {
            throw InvalidProtocol();
        }
        return value.GetString()!;
    }

    private static void RequireString(
        Dictionary<string, JsonElement> properties,
        string name,
        string expected)
    {
        if (!string.Equals(ReadString(properties, name), expected,
            StringComparison.Ordinal))
        {
            throw InvalidProtocol();
        }
    }

    private static Compiler.Core.CompilerException InvalidProtocol() =>
        ComponentException.Tool(
            "raw module inspection command returned an invalid protocol envelope");
}
