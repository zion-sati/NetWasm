// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

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
    // Each slot use's targets, in receiver order.
    ImmutableDictionary<DispatchSlot, ImmutableArray<DispatchTargetModel>> DispatchTargets,
    ImmutableDictionary<string, MethodInstanceModel> CallableMethods,
    ImmutableDictionary<EntityKey, MethodDefinitionModel> JavaScriptImports,
    ImmutableDictionary<EntityKey, MethodDefinitionModel> WitImports,
    ImmutableArray<HostCallbackDeclaration> HostCallbacks,
    ImmutableDictionary<EntityKey, JavaScriptAsyncMethodBinding> JavaScriptAsyncBindings,
    ImmutableDictionary<ManagedCallSiteKey, ManagedCallSite> ManagedCallSites)
{
    internal ImmutableDictionary<string, MethodInstanceModel> SharedObjectDispatch { get; init; } =
        ImmutableDictionary<string, MethodInstanceModel>.Empty;

    internal SharedArrayPlan? SharedArrays { get; init; }

    internal ImmutableDictionary<string, ImmutableStack<CompilerDependencyStep>> TypeDependencyPaths { get; init; } =
        ImmutableDictionary<string, ImmutableStack<CompilerDependencyStep>>.Empty;

    internal ImmutableHashSet<EnumMetadataRequirement> EnumMetadataRequirements { get; init; } = [];

    internal ImmutableHashSet<CliTypeIdentity> UninitializedObjectTypes { get; init; } = [];

    internal ImmutableHashSet<CliTypeIdentity> RuntimeTypes { get; init; } = [];

    internal ImmutableHashSet<CliTypeIdentity> RequiredTypes { get; init; } = [];

    internal ImmutableHashSet<string> GeneratedCallees { get; init; } = [];

    // Each dispatched call's admitted receivers when the closure dispatched them by receiver flow; empty when it
    // dispatched them by type.
    internal ImmutableDictionary<string, ReceiverFlow.ReceiverFlowAdmission> ReceiverFlowAdmissions { get; init; } =
        ImmutableDictionary<string, ReceiverFlow.ReceiverFlowAdmission>.Empty;

    internal ImmutableHashSet<CliTypeIdentity> StructMarshallingTypes { get; init; } = [];

    internal bool StructMarshallingFieldNames { get; init; }

    internal ImmutableDictionary<CliTypeIdentity, MethodInstanceModel> StructMarshallingConstructors { get; init; } =
        ImmutableDictionary<CliTypeIdentity, MethodInstanceModel>.Empty;

    internal ImmutableHashSet<EntityKey> ModuleInitializers { get; init; } = [];

    internal ImmutableDictionary<string, MethodContribution> Contributions { get; init; } =
        ImmutableDictionary<string, MethodContribution>.Empty;

    internal ImmutableDictionary<string, MethodInstanceModel> JavaScriptConversions { get; init; } =
        ImmutableDictionary<string, MethodInstanceModel>.Empty;

    internal bool ProducesJavaScriptErrors { get; init; }

    internal ImmutableDictionary<string, MethodInstanceModel> MethodDescriptors { get; init; } =
        ImmutableDictionary<string, MethodInstanceModel>.Empty;

    internal ImmutableDictionary<string, FieldInstanceModel> FieldDescriptors { get; init; } =
        ImmutableDictionary<string, FieldInstanceModel>.Empty;

    internal ImmutableDictionary<string, PropertyInstanceModel> PropertyDescriptors { get; init; } =
        ImmutableDictionary<string, PropertyInstanceModel>.Empty;

    internal ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan>
        ObjectArrayDelegateAdapters
    { get; init; } = ImmutableDictionary<string, ObjectArrayDelegateAdapterPlan>.Empty;

    internal MemberExecutionPlan MemberExecution { get; init; } =
        MemberExecutionPlan.Empty;

    internal ImmutableHashSet<string> NamedMemberDescriptors { get; init; } = [];

    internal bool RequiresTypeFacts { get; init; }

    internal bool RequiresGenericArguments { get; init; }

    internal bool RequiresDeclaringTypes { get; init; }

    internal bool RequiresGenericDefinitions { get; init; }

    internal bool RequiresAssemblies { get; init; }

    internal bool RequiresElementTypes { get; init; }

    internal bool RequiresTypeAttributes { get; init; }

    internal bool RequiresInterfaces { get; init; }
    internal bool RequiresManifestResources { get; init; }
    internal bool RequiresAssemblyNames { get; init; }
    internal bool RequiresAssemblyEnumeration { get; init; }


    internal ImmutableDictionary<string, MethodInstanceModel> NativeCallbacks { get; init; } =
        ImmutableDictionary<string, MethodInstanceModel>.Empty;

    // How the closure reached each host import (a WIT, JavaScript or native import method), by its definition: what a
    // diagnostic about an import names as the reason it is there.
    internal ImmutableDictionary<EntityKey, ImmutableStack<CompilerDependencyStep>> ImportDependencyPaths { get; init; } =
        ImmutableDictionary<EntityKey, ImmutableStack<CompilerDependencyStep>>.Empty;

    private static ImmutableDictionary<EntityKey, ImmutableStack<CompilerDependencyStep>> ImportPaths(ReachabilityLedger ledger)
    {
        var paths = ImmutableDictionary.CreateBuilder<EntityKey, ImmutableStack<CompilerDependencyStep>>();
        foreach (var instance in ledger.MethodInstances.Values)
        {
            var definition = instance.Definition;
            if (definition.WitImport is null && definition.JSImport is null && definition.NativeImport is null) continue;
            if (!paths.ContainsKey(definition.Key))
            {
                paths.Add(definition.Key, ledger.MethodDependencyPaths.TryGetValue(instance.CanonicalName, out var path)
                    ? path
                    : ImmutableStack<CompilerDependencyStep>.Empty);
            }
        }
        return paths.ToImmutable();
    }

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
                .ToImmutableArray()),
        ledger.CallableMethods.ToImmutable(),
        ledger.JavaScriptImports.ToImmutable(),
        ledger.WitImports.ToImmutable(),
        ledger.HostCallbacks.ToImmutable(),
            ledger.JavaScriptAsyncBindings.ToImmutable(),
            ledger.ManagedCallSites.ToImmutable())
    {
        TypeDependencyPaths = ledger.TypeDependencyPaths.ToImmutableDictionary(StringComparer.Ordinal),
        ImportDependencyPaths = ImportPaths(ledger),
        ModuleInitializers = ledger.ModuleInitializers.ToImmutable(),
        Contributions = ledger.Contributions.ToImmutableDictionary(StringComparer.Ordinal),
        JavaScriptConversions = ledger.JavaScriptConversions.ToImmutable(),
        ProducesJavaScriptErrors = ledger.ProducesJavaScriptErrors,
        EnumMetadataRequirements = ledger.EnumMetadataRequirements.ToImmutable(),
        UninitializedObjectTypes = [.. ledger.UninitializedObjectTypes],
        RuntimeTypes = [.. ledger.RuntimeTypes],
        RequiredTypes = [.. ledger.RequiredTypes],
        GeneratedCallees = [.. ledger.GeneratedCallees],
        StructMarshallingTypes = [.. ledger.StructMarshallingTypes],
        StructMarshallingFieldNames = ledger.StructMarshallingFieldNames,
        StructMarshallingConstructors = ledger.StructMarshallingConstructors.ToImmutableDictionary(),
        MethodDescriptors = ledger.MethodDescriptors.ToImmutable(),
        FieldDescriptors = ledger.FieldDescriptors.ToImmutable(),
        PropertyDescriptors = ledger.PropertyDescriptors.ToImmutable(),
        ObjectArrayDelegateAdapters = ledger.ObjectArrayDelegateAdapters.ToImmutable(),
        MemberExecution = ledger.MemberExecution,
        SharedObjectDispatch = ledger.SharedObjectDispatch.ToImmutableDictionary(StringComparer.Ordinal),
        SharedArrays = ledger.SharedArrays,
        NamedMemberDescriptors = ledger.NamedMemberDescriptors.ToImmutable(),
        RequiresTypeFacts = ledger.RequiresTypeFacts,
        RequiresGenericArguments = ledger.RequiresGenericArguments,
        RequiresDeclaringTypes = ledger.RequiresDeclaringTypes,
        RequiresGenericDefinitions = ledger.RequiresGenericDefinitions,
        RequiresAssemblies = ledger.RequiresAssemblies,
        RequiresElementTypes = ledger.RequiresElementTypes,
        RequiresTypeAttributes = ledger.RequiresTypeAttributes,
        RequiresInterfaces = ledger.RequiresInterfaces,
        RequiresManifestResources = ledger.RequiresManifestResources,
        RequiresAssemblyNames = ledger.RequiresAssemblyNames,
        RequiresAssemblyEnumeration = ledger.RequiresAssemblyEnumeration,
        NativeCallbacks = ledger.NativeCallbacks.ToImmutable(),
    };
}
