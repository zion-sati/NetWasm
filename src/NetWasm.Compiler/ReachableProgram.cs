using System.Collections.Immutable;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.IntermediateRepresentation.Delegates;

namespace NetWasm.Compiler;

public sealed record ProgramExport(string Name, EntityKey Method);

public sealed record ReachableProgram(
    MethodDefinitionModel EntryPoint,
    ImmutableDictionary<EntityKey, ManagedMethodBody> Methods,
    ImmutableHashSet<EntityKey> Types,
    ImmutableHashSet<EntityKey> Fields,
    ImmutableArray<EntityKey> StaticInitializers,
    ImmutableHashSet<string> StringLiterals,
    ImmutableArray<ProgramExport> Exports,
    ImmutableHashSet<EntityKey> AllocatingMethods,
    ImmutableDictionary<EntityKey, MethodRootMap> RootMaps,
    ImmutableDictionary<EntityKey, EntityKey> Finalizers,
    ImmutableHashSet<ManagedExceptionKind> ImplicitExceptions)
{
    public ImmutableArray<EntityKey> ModuleInitializers { get; init; } = [];

    public ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite> ManagedCallSites { get; init; } =
        ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite>.Empty;

    public ImmutableArray<ManagedDelegateBinding> DelegateBindings { get; init; } = [];

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

    public ImmutableArray<string> ConstructedStaticInitializers { get; init; } = [];

    public ImmutableDictionary<string, DispatchCallSiteModel> DispatchCallSites { get; init; } =
        [];

    public ImmutableDictionary<string, TypeTestSiteModel> TypeTestSites { get; init; } =
        [];

    public ImmutableDictionary<string, MethodInstanceModel> CallableMethods { get; init; } =
        [];

    public ImmutableArray<CliTypeIdentity> DelegateTypes { get; init; } = [];

    public ImmutableArray<MethodDefinitionModel> JSImportMethods { get; init; } = [];

    public ImmutableArray<MethodDefinitionModel> WitImportMethods { get; init; } = [];

    public ImmutableArray<HostCallbackDeclaration> HostCallbacks { get; init; } = [];

    public ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding>
        JavaScriptAsyncBindings
    { get; init; } = [];
}
