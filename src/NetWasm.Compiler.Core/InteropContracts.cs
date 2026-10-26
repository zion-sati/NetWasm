using System;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Linq;

namespace NetWasm.Compiler.Core;

/// <summary>
/// Metadata-owned host interop declaration discovered before reachability.
/// Names are opaque UTF-16 metadata strings; the emitter never synthesizes
/// consumer service names.
/// </summary>
public sealed record InteropImportDeclaration(
    string FunctionName,
    string? ModuleName)
{
    public bool IsPromise { get; init; }

    public JavaScriptMarshalAs MarshalAs { get; init; } = JavaScriptMarshalAs.None;
}

/// <summary>
/// Metadata-owned managed export declaration. A null name means the managed
/// method name is the public export name.
/// </summary>
public sealed record InteropExportDeclaration(string? ExportName)
{
    /// <summary>The method's parameter names from metadata, empty where a parameter has none.</summary>
    public ImmutableArray<string> ParameterNames { get; init; } = [];

    /// <summary>Whether each parameter is declared non-nullable; oblivious parameters are nullable.</summary>
    public ImmutableArray<bool> NonNullableParameters { get; init; } = [];

    /// <summary>Whether the result is declared non-nullable.</summary>
    public bool NonNullableResult { get; init; }

    public JavaScriptMarshalAs MarshalAs { get; init; } = JavaScriptMarshalAs.None;
}

/// <summary>
/// The [JSMarshalAs&lt;T&gt;] annotations of a JavaScript boundary's parameters and
/// result, each Mono's JSType marker as text, such as <c>Number</c>,
/// <c>Promise&lt;Number&gt;</c> or <c>Function&lt;Number,String&gt;</c>; null where a
/// position has none.
/// </summary>
public sealed record JavaScriptMarshalAs(ImmutableArray<string?> Parameters, string? Result)
{
    public static JavaScriptMarshalAs None { get; } = new([], null);

    public string? Parameter(int index) => index < Parameters.Length ? Parameters[index] : null;

    public bool Equals(JavaScriptMarshalAs? other) =>
        other is not null && Result == other.Result && Parameters.SequenceEqual(other.Parameters);

    public override int GetHashCode() => HashCode.Combine(Result, Parameters.Length);
}

/// <summary>
/// Mono's pairing of JSType markers with managed types. NetWasm takes a marker as
/// optional, keeping its default where there is none, and only a long chooses by
/// it: a Number crosses as Int52 (manifest type <c>i52</c>), exact within
/// 2^53 - 1, and a BigInt as the default <c>i64</c>.
/// </summary>
public static class JavaScriptMarshalAsRules
{
    public const string Int52 = "i52";

    /// <summary>
    /// Null when the marker fits the type; otherwise the marker the type takes.
    /// <paramref name="invoke"/> gives a delegate's Invoke signature, null for any other type.
    /// </summary>
    public static string? Mismatch(
        CliTypeIdentity type,
        string marker,
        Func<CliTypeIdentity, MethodSignatureModel?> invoke)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(marker);
        ArgumentNullException.ThrowIfNull(invoke);
        return Fits(type, Parse(marker), invoke) ? null : Expected(type, invoke);
    }

    /// <summary>Whether a marker makes a long or nullable long cross as a Number.</summary>
    public static bool SelectsInt52(CliTypeIdentity type, string? marker) =>
        marker == "Number" && JavaScriptNullable.Unwrap(type).CanonicalName == "primitive:i8";

    /// <summary>A marker as C# names it: <c>JSType.Promise&lt;JSType.Number&gt;</c>.</summary>
    public static string Describe(string marker)
    {
        ArgumentNullException.ThrowIfNull(marker);
        return marker == "no JSType" ? marker : Describe(Parse(marker));

        static string Describe(Marker marker) => marker.Arguments.IsEmpty
            ? $"JSType.{marker.Name}"
            : $"JSType.{marker.Name}<{string.Join(", ", marker.Arguments.Select(Describe))}>";
    }

    /// <summary>A marker's type arguments, such as a Promise's value or a Function's values.</summary>
    public static ImmutableArray<string> Arguments(string? marker) =>
        marker is null ? [] : [.. Parse(marker).Arguments.Select(Format)];

    private sealed record Marker(string Name, ImmutableArray<Marker> Arguments);

    private static bool Fits(CliTypeIdentity type, Marker marker, Func<CliTypeIdentity, MethodSignatureModel?> invoke)
    {
        if (JavaScriptNullable.Underlying(type) is { } underlying) type = underlying;
        if (invoke(type) is { } signature)
        {
            return marker.Name == "Function" && (marker.Arguments.IsEmpty || FitsFunction(signature, marker, invoke));
        }
        if (JavaScriptAsyncSignature.Classify(type) is { IsAsync: true } asyncReturn)
        {
            return marker is { Name: "Promise", Arguments: [var value] } &&
                (asyncReturn.ResultType is { } result
                    ? Fits(result, value, invoke)
                    : value is { Name: "Void", Arguments.IsEmpty: true });
        }
        if (type.Shape == CliTypeShape.SzArray && type.ElementType is { } element &&
            (JavaScriptValueArray.IsSupported(type) || JavaScriptConversion.IsHandleValue(type)))
        {
            return marker is { Name: "Array", Arguments: [var item] } && Fits(element, item, invoke);
        }
        return marker.Arguments.IsEmpty && Simple(type).Contains(marker.Name);
    }

    // A Function lists the delegate's parameters, then its result unless it returns void.
    private static bool FitsFunction(
        MethodSignatureModel signature,
        Marker marker,
        Func<CliTypeIdentity, MethodSignatureModel?> invoke)
    {
        var values = signature.ParameterSignatureTypes.ToList();
        if (signature.ReturnType != CliValueKind.Void) values.Add(signature.ReturnSignatureType);
        return marker.Arguments.Length == values.Count &&
            values.Zip(marker.Arguments).All(pair => Fits(pair.First, pair.Second, invoke));
    }

    private static string[] Simple(CliTypeIdentity type) => type.CanonicalName switch
    {
        "primitive:void" => ["Void", "Discard", "DiscardNoWait"],
        "primitive:bool" => ["Boolean"],
        "primitive:char" or "primitive:string" => ["String"],
        "primitive:i8" => ["Number", "BigInt"],
        "primitive:u8" => ["BigInt"],
        "primitive:object" => ["Any"],
        _ when JavaScriptDirectImport.IsScalar(type) => ["Number"],
        _ when JavaScriptConversion.IsDate(type) => ["Date"],
        _ when JavaScriptMemoryView.IsSupported(type) => ["MemoryView"],
        _ when type.FullName == "System.Exception" => ["Error"],
        _ when type.FullName == "System.Runtime.InteropServices.JavaScript.JSObject" => ["Object"],
        _ => [],
    };

    // The marker a type takes, for a mismatch's message.
    private static string Expected(CliTypeIdentity type, Func<CliTypeIdentity, MethodSignatureModel?> invoke)
    {
        if (JavaScriptNullable.Underlying(type) is { } underlying) type = underlying;
        if (invoke(type) is { } signature)
        {
            var values = signature.ParameterSignatureTypes.ToList();
            if (signature.ReturnType != CliValueKind.Void) values.Add(signature.ReturnSignatureType);
            return values.Count == 0 ? "Function" : $"Function<{string.Join(",", values.Select(value => Expected(value, invoke)))}>";
        }
        if (JavaScriptAsyncSignature.Classify(type) is { IsAsync: true } asyncReturn)
        {
            return $"Promise<{(asyncReturn.ResultType is { } result ? Expected(result, invoke) : "Void")}>";
        }
        if (type.Shape == CliTypeShape.SzArray && type.ElementType is { } element)
        {
            return $"Array<{Expected(element, invoke)}>";
        }
        return Simple(type) is [var first, ..] ? first : "no JSType";
    }

    private static Marker Parse(string text)
    {
        var position = 0;
        var marker = Read();
        return position == text.Length ? marker : new(text, []);

        Marker Read()
        {
            var start = position;
            while (position < text.Length && text[position] is not ('<' or ',' or '>')) position++;
            var name = text[start..position];
            var arguments = ImmutableArray.CreateBuilder<Marker>();
            if (position < text.Length && text[position] == '<')
            {
                do
                {
                    position++;
                    arguments.Add(Read());
                }
                while (position < text.Length && text[position] == ',');
                if (position < text.Length && text[position] == '>') position++;
            }
            return new(name, arguments.ToImmutable());
        }
    }

    private static string Format(Marker marker) => marker.Arguments.IsEmpty
        ? marker.Name
        : $"{marker.Name}<{string.Join(",", marker.Arguments.Select(Format))}>";
}

public enum JavaScriptAsyncReturnKind
{
    None,
    Task,
    ValueTask,
}

public readonly record struct JavaScriptAsyncReturn(
    JavaScriptAsyncReturnKind Kind,
    CliTypeIdentity? ResultType)
{
    public bool IsAsync => Kind != JavaScriptAsyncReturnKind.None;
    public bool HasResult => ResultType is not null;
}

public static class JavaScriptAsyncSignature
{
    public static JavaScriptAsyncReturn Classify(CliTypeIdentity returnType)
    {
        var fullName = returnType.Shape == CliTypeShape.GenericInstantiation
            ? returnType.ElementType!.FullName
            : returnType.FullName;
        var kind = fullName switch
        {
            "System.Threading.Tasks.Task" or "System.Threading.Tasks.Task`1" =>
                JavaScriptAsyncReturnKind.Task,
            "System.Threading.Tasks.ValueTask" or "System.Threading.Tasks.ValueTask`1" =>
                JavaScriptAsyncReturnKind.ValueTask,
            _ => JavaScriptAsyncReturnKind.None,
        };
        if (kind == JavaScriptAsyncReturnKind.None)
        {
            return default;
        }
        return new(
            kind,
            returnType.Shape == CliTypeShape.GenericInstantiation
                ? returnType.TypeArguments[0]
                : null);
    }
}

public static class JavaScriptAsyncAbiNames
{
    public static string Resolve(EntityKey method) =>
        $"netwasm.async.import.resolve.{method.Assembly.Name}.{method.MetadataToken:x8}";

    public static string Reject(EntityKey method) =>
        $"netwasm.async.import.reject.{method.Assembly.Name}.{method.MetadataToken:x8}";

    public static string Cancel(EntityKey method) =>
        $"netwasm.async.import.cancel.{method.Assembly.Name}.{method.MetadataToken:x8}";

    public static string ExportStatus(EntityKey method) =>
        $"netwasm.async.export.status.{method.Assembly.Name}.{method.MetadataToken:x8}";

    public static string ExportResult(EntityKey method) =>
        $"netwasm.async.export.result.{method.Assembly.Name}.{method.MetadataToken:x8}";

    public static string ExportComplete(EntityKey method) =>
        $"netwasm.async.export.complete.{method.Assembly.Name}.{method.MetadataToken:x8}";
}

public sealed record JavaScriptAsyncMethodBinding(
    EntityKey Method,
    JavaScriptAsyncReturn Return,
    CliTypeIdentity TaskType,
    MethodInstanceModel SetResult,
    MethodInstanceModel SetException,
    MethodInstanceModel SetCanceled)
{
    public FieldInstanceModel? ValueTaskTaskField { get; init; }
    public MethodInstanceModel? ValueTaskAsTask { get; init; }
    public FieldInstanceModel StatusField { get; init; } = null!;
    public FieldInstanceModel? ResultField { get; init; }
    public MethodInstanceModel? GetVoidResult { get; init; }
    // CoreLib's AsyncEntryPoint.Run for this task type, set only when the selected world has no
    // host-driven reactor: an asynchronous Main is then entered synchronously and runs its own
    // event loop to completion.
    public MethodInstanceModel? CommandEntryPoint { get; init; }
}

/// <summary>
/// The CoreLib conversions (<c>JSMarshal</c>) a JavaScript boundary applies to the
/// values it cannot pass as they are, as Mono's marshaller does. A date crosses as
/// whole Unix milliseconds in an f64; an object, an exception, or an array of
/// strings, JSObjects or objects as a host handle to its JavaScript value, which
/// its receiver releases (a handle value). Each conversion is named for its
/// direction and type, <c>ToDateTime</c> into managed code and <c>FromDateTime</c>
/// out of it, and is reachable only when a boundary passes that type that way.
/// </summary>
public static class JavaScriptConversion
{
    public const string DeclaringType = "System.Runtime.InteropServices.JavaScript.JSMarshal";
    public const string DateTimeType = "System.DateTime";
    public const string DateTimeOffsetType = "System.DateTimeOffset";

    public static bool IsDate(CliTypeIdentity type) =>
        type.FullName is DateTimeType or DateTimeOffsetType && type.IsValueType;

    /// <summary>The conversion that brings a JavaScript value into managed code as the type, if it needs one.</summary>
    public static string? ToManaged(CliTypeIdentity type) => Suffix(type) is { } suffix ? "To" + suffix : null;

    /// <summary>The conversion that takes a managed value of the type to JavaScript, if it needs one.</summary>
    public static string? FromManaged(CliTypeIdentity type) => Suffix(type) is { } suffix ? "From" + suffix : null;

    /// <summary>An object, exception or array of references: a value that crosses as a host handle.</summary>
    public static bool IsHandleValue(CliTypeIdentity type) => HandleAbiType(type) is not null;

    /// <summary>The manifest type of a handle value, otherwise null.</summary>
    public static string? HandleAbiType(CliTypeIdentity type) => type switch
    {
        _ when IsObject(type) => "any",
        _ when type.FullName == "System.Exception" => "error",
        { Shape: CliTypeShape.SzArray, ElementType: { } element } => element switch
        {
            _ when element.CanonicalName == "primitive:string" => "string[]",
            _ when element.FullName == JSObjectType => "object[]",
            _ when IsObject(element) => "any[]",
            _ => null,
        },
        _ => null,
    };

    private const string JSObjectType = "System.Runtime.InteropServices.JavaScript.JSObject";

    private static bool IsObject(CliTypeIdentity type) => type.CanonicalName == "primitive:object";

    private static string? Suffix(CliTypeIdentity type) => type switch
    {
        _ when IsDate(type) => type.FullName == DateTimeType ? "DateTime" : "DateTimeOffset",
        _ => HandleAbiType(type) switch
        {
            "any" => "Object",
            "error" => "Exception",
            "string[]" => "Strings",
            "object[]" => "Objects",
            "any[]" => "Array",
            _ => null,
        },
    };
}

/// <summary>
/// A nullable scalar or date at a JavaScript boundary, <c>T | null</c> there. A
/// parameter crosses as the pair (hasValue, value) and a result as a record of the
/// same two, hasValue as an i32 at 0 and the value at 8, where an import result
/// already lies in its descriptor.
/// </summary>
public static class JavaScriptNullable
{
    public const int HasValueOffset = 0;
    public const int ValueOffset = 8;
    public const int RecordSize = 16;

    /// <summary>The underlying scalar or date of a supported nullable, otherwise null.</summary>
    public static CliTypeIdentity? Underlying(CliTypeIdentity type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Shape == CliTypeShape.GenericInstantiation && type.ElementType?.FullName == "System.Nullable`1" &&
            type.TypeArguments is [var underlying] &&
            (JavaScriptDirectImport.IsScalar(underlying) || JavaScriptConversion.IsDate(underlying))
                ? underlying
                : null;
    }

    public static bool IsSupported(CliTypeIdentity type) => Underlying(type) is not null;

    /// <summary>The type itself, or a supported nullable's underlying type.</summary>
    public static CliTypeIdentity Unwrap(CliTypeIdentity type) => Underlying(type) ?? type;
}

public readonly record struct WitFunctionIdentity(
    string InterfaceName,
    string FunctionName);

public sealed record WitImportDeclaration(
    string InterfaceName,
    string FunctionName)
{
    public WitFunctionIdentity Identity => new(InterfaceName, FunctionName);
}

public sealed record WitExportDeclaration(
    string InterfaceName,
    string FunctionName);

public sealed record WitPostReturnDeclaration(
    string InterfaceName,
    string FunctionName);

public enum CanonicalAbiTypeKind
{
    Unit,
    Bool,
    S8,
    U8,
    S16,
    U16,
    S32,
    U32,
    S64,
    U64,
    F32,
    F64,
    Character,
    Text,
    Alias,
    List,
    Record,
    Tuple,
    Option,
    Result,
    Variant,
    Enum,
    Flags,
    OwnedResource,
    BorrowedResource,
}

public enum CanonicalAbiDirection
{
    LoweredImport,
    LiftedExport,
}

public enum CanonicalAbiFunctionKind
{
    Function,
    ImportedResourceDrop,
    ExportedResourceNew,
    ExportedResourceRep,
    ExportedResourceDrop,
    ExportedResourceDestructor,
}

public sealed record CanonicalAbiField(
    string Name,
    CanonicalAbiType Type);

public sealed record CanonicalAbiCase(
    string Name,
    CanonicalAbiType? Type);

public sealed record CanonicalAbiType(
    CanonicalAbiTypeKind Kind,
    CliTypeIdentity ManagedType)
{
    public ImmutableArray<CanonicalAbiField> Fields { get; init; } = [];
    public ImmutableArray<CanonicalAbiCase> Cases { get; init; } = [];
    public CanonicalAbiType? ElementType { get; init; }
    public CanonicalAbiType? SuccessType { get; init; }
    public CanonicalAbiType? ErrorType { get; init; }
    public int ResourceTypeId { get; init; } = -1;
    public int FlagsCount { get; init; }
}

public sealed record CanonicalAbiParameter(
    string Name,
    CanonicalAbiType Type);

public sealed record CanonicalAbiFunction(
    string InterfaceName,
    string FunctionName,
    EntityKey ManagedMethod,
    ImmutableArray<CanonicalAbiParameter> Parameters,
    CanonicalAbiType? Result)
{
    public WitFunctionIdentity Identity => new(InterfaceName, FunctionName);
    public string CoreInterfaceName { get; init; } = InterfaceName;
    public bool HasManagedBinding { get; init; } = true;
    public EntityKey? PostReturnMethod { get; init; }
    public CanonicalAbiFunctionKind Kind { get; init; }
    public string? ResourceName { get; init; }
}

public sealed record ComponentBoundaryContract(
    string Package,
    string World,
    ImmutableArray<CanonicalAbiFunction> Imports,
    ImmutableArray<CanonicalAbiFunction> Exports)
{
    public static ComponentBoundaryContract Empty { get; } = new("", "", [], []);

    public bool IsEmpty => Imports.IsEmpty && Exports.IsEmpty;
}

/// <summary>
/// Versioned, deterministic description of the bounded host boundary required
/// by one compiled application. Consumer adapters consume this instead of
/// inspecting Wasm imports directly.
/// </summary>
public sealed record HostInteropManifest(
    int Version,
    string Target,
    HostInteropStatusAbi StatusAbi,
    HostInteropTargetLayout TargetLayout,
    ImmutableArray<HostInteropImport> Imports,
    ImmutableArray<HostInteropExport> Exports)
{
    public ImmutableArray<HostInteropCallback> Callbacks { get; init; } = [];
    public ImmutableArray<HostInteropWitImport> WitImports { get; init; } = [];
    // Modules the final module may import from unchanged: those
    // [WasmImportLinkage] imports name and those the project declares for its
    // native archives. A host supplies only those the final module imports.
    public ImmutableArray<string> ImportModules { get; init; } = [];
}

public sealed record HostInteropWitImport(
    string Interface,
    string Function);

public sealed record HostInteropStatusAbi(
    int SuccessStatus,
    int HostFailureStatus,
    int ScalarResultOffset);

public sealed record HostInteropTargetLayout(
    int ManagedReferenceSize,
    int StringLengthOffset,
    int StringDataOffset,
    int ArrayLengthOffset,
    int ArrayDataPointerOffset,
    int SpanReferenceOffset,
    int SpanLengthOffset);

public sealed record HostInteropImport(
    string Module,
    string Name,
    ImmutableArray<string> Parameters,
    string Result)
{
    // "direct" when the call follows JavaScriptDirectImport, otherwise
    // "status": the import returns a status and writes its result to memory.
    public string Call { get; init; } = JavaScriptDirectImport.StatusCall;
    public string? AsyncReturn { get; init; }
    public string? ResolveExport { get; init; }
    public string? RejectExport { get; init; }
    public string? CancelExport { get; init; }
}

public sealed record HostInteropExport(
    string Name,
    ImmutableArray<string> Parameters,
    string Result)
{
    /// <summary>The managed parameter names, which generated JavaScript clients use.</summary>
    public ImmutableArray<string> ParameterNames { get; init; } = [];

    /// <summary>Whether each parameter is declared non-nullable, for generated TypeScript types.</summary>
    public ImmutableArray<bool> NonNullableParameters { get; init; } = [];

    /// <summary>Whether the result is declared non-nullable, for generated TypeScript types.</summary>
    public bool NonNullableResult { get; init; }

    public string? AsyncReturn { get; init; }
    public string? StatusExport { get; init; }
    public string? ResultExport { get; init; }
    public string? CompleteExport { get; init; }
    public string? CompletionResult { get; init; }
}

public sealed record HostInteropCallback(
    string Module,
    string ImportName,
    int ParameterIndex,
    string ExportName,
    ImmutableArray<string> Parameters,
    string Result);

public sealed record HostCallbackDeclaration(
    EntityKey ImportMethod,
    int ParameterIndex,
    MethodInstanceModel Invoke,
    string ExportName);

// A synchronous [JSImport] whose parameters and result are primitive scalars
// calls its host function directly: the arguments are the Wasm import's
// parameters and the result is its result, with no result descriptor in the
// value frame and no status to test. A host failure unwinds into the call,
// which raises JSException as a failed status does.
public static class JavaScriptDirectImport
{
    public const string StatusCall = "status";
    public const string DirectCall = "direct";

    private static readonly FrozenSet<string> Scalars = new[]
    {
        "primitive:bool", "primitive:i1", "primitive:u1", "primitive:char", "primitive:i2", "primitive:u2",
        "primitive:i4", "primitive:u4", "primitive:i8", "primitive:u8", "primitive:f4", "primitive:f8",
        "primitive:nativeint", "primitive:nativeuint",
    }.ToFrozenSet(StringComparer.Ordinal);

    public static bool IsScalar(CliTypeIdentity type) => Scalars.Contains(type.CanonicalName);

    // Delegate, string, array, object and Task types are not scalars, so a
    // callback or asynchronous import keeps the status call.
    public static bool Applies(MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(method);
        return method.JSImport is { IsPromise: false } &&
            method.Signature.ParameterSignatureTypes.All(IsScalar) &&
            (method.Signature.ReturnSignatureType.CanonicalName == "primitive:void" ||
                IsScalar(method.Signature.ReturnSignatureType));
    }
}
