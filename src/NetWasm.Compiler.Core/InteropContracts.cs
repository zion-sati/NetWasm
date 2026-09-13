using System.Collections.Immutable;

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
public sealed record InteropExportDeclaration(string? ExportName);

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
    int ArrayDataPointerOffset);

public sealed record HostInteropImport(
    string Module,
    string Name,
    ImmutableArray<string> Parameters,
    string Result)
{
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
    public string? AsyncReturn { get; init; }
    public string? StatusExport { get; init; }
    public string? ResultExport { get; init; }
    public string? CompleteExport { get; init; }
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
