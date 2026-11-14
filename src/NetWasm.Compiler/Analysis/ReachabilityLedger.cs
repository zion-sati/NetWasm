using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;

using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;

namespace NetWasm.Compiler.Analysis;

// Mutable only during one closure build. The immutable program builder is the
// sole consumer after the work queue has drained.
internal sealed class ReachabilityLedger
{
    internal ImmutableHashSet<EnumMetadataRequirement>.Builder EnumMetadataRequirements { get; } =
        ImmutableHashSet.CreateBuilder<EnumMetadataRequirement>();

    public ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite>.Builder ManagedCallSites { get; } =
        ImmutableDictionary.CreateBuilder<ManagedCallSiteKey, ManagedCallSite>();

    // What each analysed method adds to the whole program, by canonical name.
    internal Dictionary<string, MethodContribution> Contributions { get; } = new(StringComparer.Ordinal);

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
    internal ImmutableDictionary<string, MethodInstanceModel>.Builder MethodDescriptors { get; } =
        ImmutableDictionary.CreateBuilder<string, MethodInstanceModel>(StringComparer.Ordinal);
    internal ImmutableDictionary<string, FieldInstanceModel>.Builder FieldDescriptors { get; } =
        ImmutableDictionary.CreateBuilder<string, FieldInstanceModel>(StringComparer.Ordinal);
    internal ImmutableDictionary<string, PropertyInstanceModel>.Builder PropertyDescriptors { get; } =
        ImmutableDictionary.CreateBuilder<string, PropertyInstanceModel>(StringComparer.Ordinal);
    internal ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan>.Builder
        ObjectArrayDelegateAdapters
    { get; } = ImmutableDictionary.CreateBuilder<
        string,
        ObjectArrayDelegateAdapterPlan>(StringComparer.Ordinal);
    internal MemberExecutionPlan MemberExecution { get; set; } =
        MemberExecutionPlan.Empty;
    internal ImmutableHashSet<string>.Builder NamedMemberDescriptors { get; } =
        ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
    internal bool RequiresTypeFacts { get; set; }
    internal bool RequiresGenericArguments { get; set; }
    internal bool RequiresDeclaringTypes { get; set; }
    internal bool RequiresGenericDefinitions { get; set; }
    internal bool RequiresAssemblies { get; set; }
    internal bool RequiresElementTypes { get; set; }
    internal bool RequiresTypeAttributes { get; set; }
    internal bool RequiresInterfaces { get; set; }
    internal bool RequiresMemberNames { get; set; }
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
    internal ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Builder Finalizers
    { get; } = ImmutableDictionary.CreateBuilder<CliTypeIdentity, MethodInstanceModel>();
    internal ImmutableHashSet<ManagedExceptionKind>.Builder ImplicitExceptions { get; } =
        ImmutableHashSet.CreateBuilder<ManagedExceptionKind>();
    internal Dictionary<string, ImmutableStack<CompilerDependencyStep>> MethodDependencyPaths { get; } =
        new(StringComparer.Ordinal);
    internal Dictionary<string, ImmutableStack<CompilerDependencyStep>> DispatchDependencyPaths { get; } =
        new(StringComparer.Ordinal);
    internal Dictionary<string, ImmutableStack<CompilerDependencyStep>> TypeDependencyPaths { get; } =
        new(StringComparer.Ordinal);
    internal Queue<MethodInstanceModel> Pending { get; } = new();
    internal Queue<(string DispatchKey, DispatchDeclaration Declaration,
        CliTypeIdentity Receiver)> PendingDispatches
    { get; } = new();
    internal HashSet<string> Discovered { get; } = new(StringComparer.Ordinal);
    internal HashSet<string> ConstructedIdentities { get; } = new(StringComparer.Ordinal);
    internal HashSet<CliTypeIdentity> AllocatedTypes { get; } = [];
    internal HashSet<CliTypeIdentity> UninitializedObjectTypes { get; } = [];

    // Types that only managed optimization kept out of the program (ReachableMethodAnalysis.RequiredTypes).
    internal HashSet<CliTypeIdentity> RequiredTypes { get; } = [];
    internal HashSet<CliTypeIdentity> StructMarshallingTypes { get; } = [];
    internal bool StructMarshallingFieldNames { get; set; }
    internal Dictionary<string, DispatchDeclaration> DispatchDeclarations { get; } =
        new(StringComparer.Ordinal);
    // Object's virtual calls whose many-target sites call a shared body, by declaration.
    internal Dictionary<string, MethodInstanceModel> SharedObjectDispatch { get; } =
        new(StringComparer.Ordinal);
    internal Dictionary<string, Dictionary<string, DispatchTargetModel>> DispatchTargets
    { get; } = new(StringComparer.Ordinal);
    internal ImmutableDictionary<string, MethodInstanceModel>.Builder CallableMethods { get; } =
        ImmutableDictionary.CreateBuilder<string, MethodInstanceModel>(StringComparer.Ordinal);
    internal ImmutableDictionary<string, MethodInstanceModel>.Builder NativeCallbacks { get; } =
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
    internal ImmutableDictionary<string, MethodInstanceModel>.Builder JavaScriptConversions { get; } =
        ImmutableDictionary.CreateBuilder<string, MethodInstanceModel>(StringComparer.Ordinal);
}
