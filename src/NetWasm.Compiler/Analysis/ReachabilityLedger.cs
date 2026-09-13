using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Analysis;

// Mutable only during one closure build. The immutable program builder is the
// sole consumer after the work queue has drained.
internal sealed class ReachabilityLedger
{
    public ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite>.Builder ManagedCallSites { get; } =
        ImmutableDictionary.CreateBuilder<ManagedCallSiteKey, ManagedCallSite>();

    internal ImmutableDictionary<EntityKey, ManagedMethodBody>.Builder Methods { get; } =
        ImmutableDictionary.CreateBuilder<EntityKey, ManagedMethodBody>();
    internal ImmutableDictionary<string, ManagedMethodBody>.Builder ConstructedMethods { get; } =
        ImmutableDictionary.CreateBuilder<string, ManagedMethodBody>(StringComparer.Ordinal);
    internal ImmutableDictionary<string, MethodInstanceModel>.Builder MethodInstances { get; } =
        ImmutableDictionary.CreateBuilder<string, MethodInstanceModel>(StringComparer.Ordinal);
    internal ImmutableHashSet<CliTypeIdentity>.Builder ConstructedTypes { get; } =
        ImmutableHashSet.CreateBuilder<CliTypeIdentity>();
    internal ImmutableHashSet<EntityKey>.Builder RuntimeTypeDefinitions { get; } =
        ImmutableHashSet.CreateBuilder<EntityKey>();
    internal ImmutableDictionary<string, FieldInstanceModel>.Builder ConstructedFields { get; } =
        ImmutableDictionary.CreateBuilder<string, FieldInstanceModel>(StringComparer.Ordinal);
    internal ImmutableHashSet<EntityKey>.Builder Types { get; } =
        ImmutableHashSet.CreateBuilder<EntityKey>();
    internal ImmutableHashSet<EntityKey>.Builder Fields { get; } =
        ImmutableHashSet.CreateBuilder<EntityKey>();
    internal ImmutableHashSet<string>.Builder Strings { get; } =
        ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
    internal ImmutableHashSet<EntityKey>.Builder StaticInitializers { get; } =
        ImmutableHashSet.CreateBuilder<EntityKey>();
    internal ImmutableHashSet<EntityKey>.Builder ModuleInitializers { get; } =
        ImmutableHashSet.CreateBuilder<EntityKey>();
    internal ImmutableHashSet<string>.Builder ConstructedStaticInitializers { get; } =
        ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
    internal ImmutableDictionary<EntityKey, EntityKey>.Builder Finalizers { get; } =
        ImmutableDictionary.CreateBuilder<EntityKey, EntityKey>();
    internal ImmutableHashSet<ManagedExceptionKind>.Builder ImplicitExceptions { get; } =
        ImmutableHashSet.CreateBuilder<ManagedExceptionKind>();
    internal Queue<MethodInstanceModel> Pending { get; } = new();
    internal Queue<(string DispatchKey, DispatchDeclaration Declaration,
        CliTypeIdentity Receiver)> PendingDispatches
    { get; } = new();
    internal HashSet<string> Discovered { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> ConstructedIdentities { get; } = new(StringComparer.Ordinal);
    internal HashSet<CliTypeIdentity> AllocatedTypes { get; } = [];
    internal Dictionary<string, DispatchDeclaration> DispatchDeclarations { get; } =
        new(StringComparer.Ordinal);
    internal Dictionary<string, Dictionary<string, DispatchTargetModel>> DispatchTargets
    { get; } = new(StringComparer.Ordinal);
    internal ImmutableDictionary<string, MethodInstanceModel>.Builder CallableMethods { get; } =
        ImmutableDictionary.CreateBuilder<string, MethodInstanceModel>(StringComparer.Ordinal);
    internal ImmutableDictionary<EntityKey, MethodDefinitionModel>.Builder JavaScriptImports { get; } =
        ImmutableDictionary.CreateBuilder<EntityKey, MethodDefinitionModel>();
    internal ImmutableDictionary<EntityKey, MethodDefinitionModel>.Builder WitImports { get; } =
        ImmutableDictionary.CreateBuilder<EntityKey, MethodDefinitionModel>();
    internal ImmutableArray<HostCallbackDeclaration>.Builder HostCallbacks { get; } =
        ImmutableArray.CreateBuilder<HostCallbackDeclaration>();
    internal ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>.Builder
        JavaScriptAsyncBindings
    { get; } =
        ImmutableDictionary.CreateBuilder<EntityKey, JavaScriptAsyncMethodBinding>();
}
