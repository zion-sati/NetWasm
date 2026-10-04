using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Analysis;

internal sealed record ReachabilityExceptionRequirement(
    ManagedExceptionKind Kind,
    string TypeName);

internal sealed record ReachabilityMethodReference(
    CilOperation Operation,
    MethodInstanceModel Method);

internal sealed record ReachabilityEntityReference(
    CilOperation Operation,
    EntityKey Entity);

internal sealed record ReachabilityDispatch(
    string Key,
    DispatchDeclaration Declaration);

internal sealed record ReachabilityInstructionAnalysis(
    ImmutableArray<CliTypeIdentity> RuntimeTypes,
    ImmutableArray<CliTypeIdentity> ConstructedTypes,
    ImmutableArray<CliTypeIdentity> AllocatedTypes,
    ImmutableArray<EntityKey> Types,
    ImmutableArray<string> Strings,
    ImmutableArray<ReachabilityMethodReference> Methods,
    ImmutableArray<ReachabilityEntityReference> Entities,
    ImmutableArray<FieldInstanceModel> Fields,
    ImmutableArray<ReachabilityDispatch> Dispatches,
    ImmutableArray<MethodInstanceModel> CallableMethods,
    ImmutableArray<ManagedCallSite> CallSites = default)
{
    public ImmutableArray<MethodInstanceModel> MethodDescriptors { get; init; } = [];

    public ImmutableArray<FieldInstanceModel> FieldDescriptors { get; init; } = [];

    public ImmutableArray<MethodInstanceModel> NativeCallbacks { get; init; } = [];

    public bool RequiresTypeFacts { get; init; }

    public bool RequiresDelegateInvoke { get; init; }

    public bool RequiresGenericArguments { get; init; }

    public bool RequiresMemberNames { get; init; }

    public RuntimeTypeNamePayload TypeNamePayload { get; init; }
}

internal sealed record ReachableMethodAnalysis(
    MethodInstanceModel Method,
    ManagedMethodBody Body,
    ImmutableArray<EntityKey> CatchTypes,
    ImmutableArray<ReachabilityExceptionRequirement> Exceptions,
    ReachabilityInstructionAnalysis Instructions);

internal sealed record ReachabilityImportAnalysis(
    bool IsHandled,
    ImmutableArray<CliTypeIdentity> ConstructedTypes,
    ImmutableArray<CliTypeIdentity> AllocatedTypes,
    ImmutableArray<EntityKey> Types,
    ImmutableArray<FieldInstanceModel> Fields,
    ImmutableArray<MethodInstanceModel> EnqueuedMethods,
    ImmutableArray<ReachabilityExceptionRequirement> Exceptions,
    MethodDefinitionModel? JavaScriptImport,
    MethodDefinitionModel? WitImport,
    ImmutableArray<HostCallbackDeclaration> HostCallbacks,
    JavaScriptAsyncMethodBinding? JavaScriptAsyncBinding)
{
    public ImmutableArray<EnumMetadataRequirement> EnumMetadataRequirements { get; init; } = [];
}
