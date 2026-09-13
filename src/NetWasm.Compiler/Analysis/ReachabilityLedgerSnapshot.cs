using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

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
    ImmutableDictionary<EntityKey, EntityKey> Finalizers,
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
    internal ImmutableHashSet<EntityKey> ModuleInitializers { get; init; } = [];

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
    };
}
