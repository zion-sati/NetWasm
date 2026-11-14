using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;

namespace NetWasm.Compiler;

public sealed record ProgramExport(string Name, EntityKey Method);

public sealed record ReachableProgram(
    MethodDefinitionModel? EntryPoint,
    ImmutableDictionary<EntityKey, ManagedMethodBody> Methods,
    ImmutableHashSet<EntityKey> Types,
    ImmutableHashSet<EntityKey> Fields,
    ImmutableArray<EntityKey> StaticInitializers,
    ImmutableHashSet<string> StringLiterals,
    ImmutableArray<ProgramExport> Exports,
    ImmutableHashSet<EntityKey> AllocatingMethods,
    ImmutableDictionary<EntityKey, MethodRootMap> RootMaps,
    ImmutableDictionary<CliTypeIdentity, MethodInstanceModel> Finalizers,
    ImmutableHashSet<ManagedExceptionKind> ImplicitExceptions)
{
    internal ImmutableDictionary<string, ImmutableStack<CompilerDependencyStep>> TypeDependencyPaths { get; init; } =
        ImmutableDictionary<string, ImmutableStack<CompilerDependencyStep>>.Empty;

    public ImmutableHashSet<EnumMetadataRequirement> EnumMetadataRequirements { get; init; } = [];

    // Types RuntimeHelpers.GetUninitializedObject creates; empty unless it is reachable.
    public ImmutableArray<CliTypeIdentity> UninitializedObjectTypes { get; init; } = [];

    // Types that receive structure-marshalling descriptors, and whether those descriptors carry
    // field names for Marshal.OffsetOf; empty unless Marshal's structure members are reachable.
    public ImmutableArray<CliTypeIdentity> StructMarshallingTypes { get; init; } = [];

    public bool StructMarshallingFieldNames { get; init; }

    public ImmutableArray<EntityKey> ModuleInitializers { get; init; } = [];

    // What each analysed method adds to the whole program, by canonical name: an edit that
    // keeps every changed method's contribution leaves the rest of the program as it was.
    internal ImmutableDictionary<string, NetWasm.Compiler.Analysis.MethodContribution> MethodContributions { get; init; } =
        ImmutableDictionary<string, NetWasm.Compiler.Analysis.MethodContribution>.Empty;

    public ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite> ManagedCallSites { get; init; } =
        ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite>.Empty;

    public ImmutableArray<ManagedDelegateBinding> DelegateBindings { get; init; } = [];

    public ManagedByRefEffects ManagedByRefEffects { get; init; } =
        ManagedByRefEffects.Empty;

    public ImmutableDictionary<string, ManagedMethodBody> ConstructedMethods { get; init; } =
        [];

    public ImmutableDictionary<string, MethodInstanceModel> MethodInstances { get; init; } =
        [];

    public ImmutableHashSet<EntityKey> RuntimeTypeDefinitions { get; init; } = [];

    public ImmutableHashSet<CliTypeIdentity> ConstructedTypes { get; init; } =
        [];

    public ImmutableHashSet<string> ConstructedAllocatingMethods { get; init; } =
        [];

    public ImmutableDictionary<string, MethodRootMap> ConstructedRootMaps { get; init; } =
        [];

    public ImmutableDictionary<string, FieldInstanceModel> ConstructedFields { get; init; } =
        [];

    public ImmutableDictionary<string, MethodInstanceModel> MethodDescriptors { get; init; } =
        [];

    public ImmutableDictionary<string, FieldInstanceModel> FieldDescriptors { get; init; } =
        [];

    public ImmutableDictionary<string, PropertyInstanceModel> PropertyDescriptors { get; init; } =
        [];

    public ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan>
        ObjectArrayDelegateAdapters
    { get; init; } = [];

    public MemberExecutionPlan MemberExecution { get; init; } =
        MemberExecutionPlan.Empty;

    public ImmutableHashSet<string> NamedMemberDescriptors { get; init; } = [];

    public bool RequiresTypeFacts { get; init; }


    public bool RequiresGenericArguments { get; init; }

    public bool RequiresDeclaringTypes { get; init; }

    public bool RequiresGenericDefinitions { get; init; }

    public bool RequiresAssemblies { get; init; }

    public bool RequiresElementTypes { get; init; }

    public bool RequiresTypeAttributes { get; init; }

    public bool RequiresInterfaces { get; init; }


    public ImmutableArray<string> ConstructedStaticInitializers { get; init; } = [];

    public ImmutableDictionary<string, DispatchCallSiteModel> DispatchCallSites { get; init; } =
        [];

    // The shared body each of object's virtual calls uses at its many-target sites, by declaration
    // (SharedDispatchPolicy).
    public ImmutableDictionary<string, MethodInstanceModel> SharedObjectDispatch { get; init; } =
        ImmutableDictionary<string, MethodInstanceModel>.Empty;

    public ImmutableDictionary<string, TypeTestSiteModel> TypeTestSites { get; init; } =
        [];

    public ImmutableDictionary<string, MethodInstanceModel> CallableMethods { get; init; } =
        [];

    public ImmutableDictionary<string, MethodInstanceModel> NativeCallbacks { get; init; } =
        [];

    public ImmutableArray<CliTypeIdentity> DelegateTypes { get; init; } = [];

    // Every type the program allocates, by canonical name: the receivers dispatch and type
    // tests are resolved against.
    public ImmutableArray<CliTypeIdentity> AllocatedTypes { get; init; } = [];

    public ImmutableArray<MethodDefinitionModel> JSImportMethods { get; init; } = [];

    public ImmutableArray<MethodDefinitionModel> WitImportMethods { get; init; } = [];

    public ImmutableArray<HostCallbackDeclaration> HostCallbacks { get; init; } = [];

    public ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>
        JavaScriptAsyncBindings
    { get; init; } = [];

    // CoreLib's conversions, by name, of the values reachable JavaScript boundaries convert.
    public ImmutableDictionary<string, MethodInstanceModel> JavaScriptConversions { get; init; } =
        ImmutableDictionary<string, MethodInstanceModel>.Empty;
}
