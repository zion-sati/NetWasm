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
    // Modules whose imports the host supplies to the final module unchanged:
    // those [WasmImportLinkage] imports name and those the project declares
    // for its native archives.
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
