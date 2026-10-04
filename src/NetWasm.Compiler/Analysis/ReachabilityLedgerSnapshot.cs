using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;

using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Analysis;

internal sealed record ReachabilityLedgerSnapshot(
    ImmutableDictionary<EntityKey, ManagedMethodBody> Methods,
    ImmutableDictionary<string, ManagedMethodBody> ConstructedMethods,
    ImmutableDictionary<string, MethodInstanceModel> MethodInstances,
    ImmutableHashSet<CliTypeIdentity> ConstructedTypes,
    ImmutableHashSet<EntityKey> RuntimeTypeDefinitions,
    ImmutableDictionary<string, FieldInstanceModel> ConstructedFields,
    ImmutableHashSet<EntityKey> Types,
    ImmutableHashSet<EntityKey> Fields,
    ImmutableHashSet<string> Strings,
    ImmutableHashSet<EntityKey> StaticInitializers,
    ImmutableHashSet<string> ConstructedStaticInitializers,
    ImmutableDictionary<CliTypeIdentity, MethodInstanceModel> Finalizers,
    ImmutableHashSet<ManagedExceptionKind> ImplicitExceptions,
    ImmutableHashSet<CliTypeIdentity> AllocatedTypes,
    ImmutableDictionary<string, DispatchDeclaration> DispatchDeclarations,
    ImmutableDictionary<string, ImmutableArray<DispatchTargetModel>> DispatchTargets,
    ImmutableDictionary<string, MethodInstanceModel> CallableMethods,
    ImmutableDictionary<EntityKey, MethodDefinitionModel> JavaScriptImports,
    ImmutableDictionary<EntityKey, MethodDefinitionModel> WitImports,
    ImmutableArray<HostCallbackDeclaration> HostCallbacks,
    ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding> JavaScriptAsyncBindings,
    ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite> ManagedCallSites)
{
    internal ImmutableHashSet<EnumMetadataRequirement> EnumMetadataRequirements { get; init; } = [];

    internal ImmutableHashSet<EntityKey> ModuleInitializers { get; init; } = [];

    internal ImmutableDictionary<string, MethodInstanceModel> MethodDescriptors { get; init; } =
        ImmutableDictionary<string, MethodInstanceModel>.Empty;

    internal ImmutableDictionary<string, FieldInstanceModel> FieldDescriptors { get; init; } =
        ImmutableDictionary<string, FieldInstanceModel>.Empty;

    internal ImmutableDictionary<string, PropertyInstanceModel> PropertyDescriptors { get; init; } =
        ImmutableDictionary<string, PropertyInstanceModel>.Empty;

    internal ImmutableDictionary<string, MethodInstanceModel> DelegateInvokeDescriptors
    { get; init; } = ImmutableDictionary<string, MethodInstanceModel>.Empty;

    internal ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan>
        ObjectArrayDelegateAdapters
    { get; init; } = ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan>.Empty;

    internal MemberExecutionPlan MemberExecution { get; init; } =
        MemberExecutionPlan.Empty;

    internal ImmutableHashSet<string> NamedMemberDescriptors { get; init; } = [];

    internal bool RequiresTypeFacts { get; init; }

    internal bool RequiresDelegateInvoke { get; init; }

    internal bool RequiresGenericArguments { get; init; }

    internal RuntimeTypeNamePayload TypeNamePayload { get; init; }

    internal ImmutableDictionary<string, MethodInstanceModel> NativeCallbacks { get; init; } =
        ImmutableDictionary<string, MethodInstanceModel>.Empty;

    internal static ReachabilityLedgerSnapshot From(ReachabilityLedger ledger) => new(
        ledger.Methods.ToImmutable(),
        ledger.ConstructedMethods.ToImmutable(),
        ledger.MethodInstances.ToImmutable(),
        ledger.ConstructedTypes.ToImmutable(),
        ledger.RuntimeTypeDefinitions.ToImmutable(),
        ledger.ConstructedFields.ToImmutable(),
        ledger.Types.ToImmutable(),
        ledger.Fields.ToImmutable(),
        ledger.Strings.ToImmutable(),
        ledger.StaticInitializers.ToImmutable(),
        ledger.ConstructedStaticInitializers.ToImmutable(),
        ledger.Finalizers.ToImmutable(),
        ledger.ImplicitExceptions.ToImmutable(),
        ImmutableHashSet.CreateRange(ledger.AllocatedTypes),
        ledger.DispatchDeclarations.ToImmutableDictionary(StringComparer.Ordinal),
        ledger.DispatchTargets.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value.Values
                .OrderBy(target => target.ReceiverType.CanonicalName, StringComparer.Ordinal)
                .ThenBy(target => target.Method.CanonicalName, StringComparer.Ordinal)
                .ToImmutableArray(),
            StringComparer.Ordinal),
        ledger.CallableMethods.ToImmutable(),
        ledger.JavaScriptImports.ToImmutable(),
        ledger.WitImports.ToImmutable(),
        ledger.HostCallbacks.ToImmutable(),
            ledger.JavaScriptAsyncBindings.ToImmutable(),
            ledger.ManagedCallSites.ToImmutable())
    {
        ModuleInitializers = ledger.ModuleInitializers.ToImmutable(),
        EnumMetadataRequirements = ledger.EnumMetadataRequirements.ToImmutable(),
        MethodDescriptors = ledger.MethodDescriptors.ToImmutable(),
        FieldDescriptors = ledger.FieldDescriptors.ToImmutable(),
        PropertyDescriptors = ledger.PropertyDescriptors.ToImmutable(),
        DelegateInvokeDescriptors = ledger.DelegateInvokeDescriptors.ToImmutable(),
        ObjectArrayDelegateAdapters = ledger.ObjectArrayDelegateAdapters.ToImmutable(),
        MemberExecution = ledger.MemberExecution,
        NamedMemberDescriptors = ledger.NamedMemberDescriptors.ToImmutable(),
        RequiresTypeFacts = ledger.RequiresTypeFacts,
        RequiresDelegateInvoke = ledger.RequiresDelegateInvoke,
        RequiresGenericArguments = ledger.RequiresGenericArguments,
        TypeNamePayload = ledger.TypeNamePayload,
        NativeCallbacks = ledger.NativeCallbacks.ToImmutable(),
    };
}
