using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NetWasm.Hosting.Build;

namespace NetWasm.Hosting.Build.JavaScript;

/// <summary>How one realm exposes a JSExport to JavaScript callers.</summary>
internal interface IJavaScriptExportRealm
{
    /// <summary>Names a generated host may not use for an export.</summary>
    IReadOnlySet<string> ReservedNames { get; }

    /// <summary>The TypeScript member that declares <paramref name="export"/>.</summary>
    string Declare(WorkerJavaScriptExport export);

    /// <summary>Why this realm cannot publish <paramref name="export"/>, or null when it can.</summary>
    string? DescribeUnsupported(WorkerJavaScriptExport export);

    /// <summary>The parameter names this realm's generated members use for <paramref name="export"/>.</summary>
    ImmutableArray<string> ParameterNames(WorkerJavaScriptExport export);
}

/// <summary>An export uses a value its host realm cannot pass.</summary>
internal sealed class JavaScriptExportRealmException(string message) : ArgumentException(message);

/// <summary>A page session calls exports in the same realm: synchronous exports return directly.</summary>
internal sealed class PageJavaScriptExportRealm : IJavaScriptExportRealm
{
    public static PageJavaScriptExportRealm Instance { get; } = new();

    public IReadOnlySet<string> ReservedNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "__proto__",
        "close",
        "constructor",
        "failure",
        "prototype",
        "then",
    };

    // An asynchronous export takes a trailing { signal }, as a worker call does.
    public string Declare(WorkerJavaScriptExport export)
    {
        var result = JavaScriptExportSurface.TypeScriptType(export.Result, allowVoid: true, !export.NonNullableResult);
        return export.AsyncReturn is null
            ? $"{JavaScriptExportSurface.Signature(export, ParameterNames(export), [])}: {result};"
            : $"{JavaScriptExportSurface.Signature(export, ParameterNames(export), ["options?: NetWasmCallOptions"])}: "
                + $"Promise<{result}>;";
    }

    public string? DescribeUnsupported(WorkerJavaScriptExport export) => null;

    public ImmutableArray<string> ParameterNames(WorkerJavaScriptExport export) =>
        JavaScriptParameterNames.Resolve(export.ParameterNames, export.Parameters.Length,
            export.AsyncReturn is null ? [] : ["options"]);
}

/// <summary>A worker client posts every call, so each export returns a promise and takes call options.</summary>
internal sealed class WorkerJavaScriptExportRealm : IJavaScriptExportRealm
{
    public static WorkerJavaScriptExportRealm Instance { get; } = new();

    public IReadOnlySet<string> ReservedNames { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "__proto__",
        "constructor",
        "dispose",
        "prototype",
        "terminate",
        "then",
    };

    public string Declare(WorkerJavaScriptExport export) =>
        $"{JavaScriptExportSurface.Signature(export, ParameterNames(export), ["options?: NetWasmWorkerCallOptions"])}: "
        + $"Promise<{JavaScriptExportSurface.TypeScriptType(export.Result, allowVoid: true, !export.NonNullableResult)}>;";

    // The generated client's arrow functions take a trailing options argument and call session.
    public ImmutableArray<string> ParameterNames(WorkerJavaScriptExport export) =>
        JavaScriptParameterNames.Resolve(export.ParameterNames, export.Parameters.Length, ["options", "session"]);

    // A page object cannot be posted to a worker, and a worker's object cannot be posted back.
    public string? DescribeUnsupported(WorkerJavaScriptExport export) =>
        export.Parameters.Any(IsRealmBound) || IsRealmBound(export.Result)
            ? $"JavaScript export '{export.Name}' passes a JSObject, which cannot cross into a Web Worker. "
                + "Publish it with the page host (NetWasmJavaScriptHosts=page)."
            : null;

    private static bool IsRealmBound(string type) => type is "object" or "object[]";
}

/// <summary>The export naming and type mapping every generated host shares.</summary>
internal static class JavaScriptExportSurface
{
    public static void AppendMembers(
        StringBuilder source,
        IJavaScriptExportRealm realm,
        IEnumerable<WorkerJavaScriptExport> exports)
    {
        foreach (var export in exports) source.Append("  ").AppendLine(realm.Declare(export));
    }

    public static string Signature(
        WorkerJavaScriptExport export,
        ImmutableArray<string> names,
        IEnumerable<string> trailing)
    {
        var parameters = export.Parameters
            .Select((type, index) =>
                $"{names[index]}: {TypeScriptType(type, allowVoid: false, export.IsNullableParameter(index))}")
            .Concat(trailing);
        return $"{JsonSerializer.Serialize(export.Name, HostingBuildJsonContext.Default.String)}({string.Join(", ", parameters)})";
    }

    public static void Validate(IJavaScriptExportRealm realm, IEnumerable<WorkerJavaScriptExport> exports)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var export in exports)
        {
            ArgumentNullException.ThrowIfNull(export);
            if (string.IsNullOrWhiteSpace(export.Name) || realm.ReservedNames.Contains(export.Name)
                || !names.Add(export.Name) || export.Parameters.IsDefault
                || export.AsyncReturn is not null and not "task" and not "value-task")
            {
                throw new ArgumentException($"JavaScript export '{export.Name}' is invalid for this host.");
            }
            foreach (var parameter in export.Parameters) TypeScriptType(parameter, allowVoid: false);
            TypeScriptType(export.Result, allowVoid: true);
            if (realm.DescribeUnsupported(export) is { } reason) throw new JavaScriptExportRealmException(reason);
        }
    }

    /// <summary>
    /// Maps an ABI type to TypeScript. A string, array or JSObject is <c>| null</c>
    /// unless C# declares it non-nullable.
    /// </summary>
    public static string TypeScriptType(string value, bool allowVoid, bool nullable = true)
    {
        // A nullable scalar or date, "i32?", is the scalar or null.
        if (value.EndsWith('?')) return TypeScriptType(value[..^1], allowVoid: false) + " | null";
        var reference = value switch
        {
            "string" => "string",
            "object" => "object",
            "bytes" => "Uint8Array",
            "i8[]" => "Int8Array",
            "i16[]" => "Int16Array",
            "u16[]" => "Uint16Array",
            "i32[]" => "Int32Array",
            "u32[]" => "Uint32Array",
            "i64[]" => "BigInt64Array",
            "u64[]" => "BigUint64Array",
            "f32[]" => "Float32Array",
            "f64[]" => "Float64Array",
            "error" => "Error",
            "string[]" => "(string | null)[]",
            "object[]" => "(object | null)[]",
            "any[]" => "unknown[]",
            _ => null,
        };
        if (reference is not null) return nullable ? reference + " | null" : reference;
        return value switch
        {
            "void" when allowVoid => "void",
            "any" => "unknown",
            "bool" => "boolean",
            "char" => "string",
            "i64" or "u64" => "bigint",
            "i8" or "u8" or "i16" or "u16" or "i32" or "u32" or "f32" or "f64" or "i52" => "number",
            "date" => "Date",
            _ => throw new ArgumentException($"JavaScript export ABI type '{value}' is unsupported."),
        };
    }
}
