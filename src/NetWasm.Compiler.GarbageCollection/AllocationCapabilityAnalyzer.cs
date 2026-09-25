using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed class AllocationCapabilityAnalyzer(
    ITypeRepository types,
    IFieldRepository fields,
    IMethodRepository methods,
    IRuntimeAllocationSafepointClassifier runtimeSafepoints) : IAllocationCapabilityAnalyzer
{
    private readonly ITypeRepository _types =
        types ?? throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepository _fields =
        fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IMethodRepository _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IRuntimeAllocationSafepointClassifier _runtimeSafepoints =
        runtimeSafepoints ?? throw new ArgumentNullException(nameof(runtimeSafepoints));

    public AllocationCapabilities Analyze(AllocationCapabilityAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Methods);
        ArgumentNullException.ThrowIfNull(request.ConstructedMethods);
        ArgumentNullException.ThrowIfNull(request.DispatchCallSites);

        var directIdentities = request.Methods.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Method.CanonicalName);
        var bodies = request.Methods
            .Select(pair => (Identity: directIdentities[pair.Key], pair.Value))
            .Concat(request.ConstructedMethods.Select(pair =>
                (Identity: pair.Key, pair.Value)))
            .ToDictionary(
                pair => pair.Identity,
                pair => pair.Value,
                StringComparer.Ordinal);
        var directInitializers = request.Methods
            .Where(pair => pair.Value.Method.Definition.Name == ".cctor")
            .ToDictionary(
                pair => pair.Value.Method.Definition.DeclaringType,
                pair => directIdentities[pair.Key]);
        var constructedInitializers = request.ConstructedMethods
            .Where(pair => pair.Value.Method.Definition.Name == ".cctor")
            .ToDictionary(
                pair => pair.Value.Method.DeclaringType.CanonicalName,
                pair => pair.Key,
                StringComparer.Ordinal);
        var allocating = bodies
            // Cached-failure dispatch can run allocating exception filters even
            // when the initializer's managed body is allocation-free.
            .Where(pair => pair.Value.Method.Definition.Name == ".cctor" ||
                Instructions(pair.Value).Any(instruction =>
                CilSafepointClassifier.RequiresUnconditionalRootDecision(instruction)))
            .Select(pair => pair.Key)
            .ToHashSet(StringComparer.Ordinal);

        bool changed;
        do
        {
            changed = false;
            foreach ((var identity, var body) in bodies)
            {
                if (allocating.Contains(identity))
                {
                    continue;
                }
                var callsAllocator = Instructions(body).Any(instruction =>
                    CallsAllocatingMethod(
                        identity,
                        instruction,
                        allocating,
                        directIdentities,
                        directInitializers,
                        constructedInitializers,
                        request.DispatchCallSites));
                if (callsAllocator)
                {
                    changed |= allocating.Add(identity);
                }
            }
        }
        while (changed);

        return new AllocationCapabilities(
            [..
                request.Methods
                    .Where(pair => allocating.Contains(directIdentities[pair.Key]))
                    .Select(pair => pair.Key)],
            request.ConstructedMethods.Keys
                .Where(allocating.Contains)
                .ToImmutableHashSet(StringComparer.Ordinal));
    }

    private static ImmutableArray<CilInstruction> Instructions(ManagedMethodBody method) =>
        method.Body.Instructions;

    private bool CallsAllocatingMethod(
        string caller,
        CilInstruction instruction,
        HashSet<string> allocating,
        Dictionary<EntityKey, string> directIdentities,
        Dictionary<EntityKey, string> directInitializers,
        Dictionary<string, string> constructedInitializers,
        IReadOnlyDictionary<string, DispatchCallSiteModel> dispatchCallSites)
    {
        if (instruction.Operation is CilOperation.LoadStaticField or
            CilOperation.LoadStaticFieldAddress or CilOperation.StoreStaticField)
        {
            string? initializer;
            if (instruction.Operand is CilOperand.FieldInstance field)
            {
                if (constructedInitializers.TryGetValue(
                        field.Value.DeclaringType.CanonicalName,
                        out var constructed))
                {
                    initializer = constructed;
                }
                else if (directInitializers.TryGetValue(
                             field.Value.Definition.DeclaringType,
                             out var direct))
                {
                    initializer = direct;
                }
                else
                {
                    initializer = null;
                }
            }
            else
            {
                var fieldDefinition = _fields.GetField(
                    ((CilOperand.Entity)instruction.Operand).Key);
                initializer = directInitializers.TryGetValue(
                    fieldDefinition.DeclaringType,
                    out var entityDirect)
                    ? entityDirect
                    : null;
            }
            return initializer is not null && allocating.Contains(initializer);
        }
        if (instruction.Operation is not (CilOperation.Call or CilOperation.CallVirtual))
        {
            return false;
        }
        var dispatchKey = $"{caller}@{instruction.Offset:x8}";
        if (dispatchCallSites.TryGetValue(dispatchKey, out var dispatch))
        {
            return dispatch.Targets.Any(target =>
                allocating.Contains(target.Method.CanonicalName));
        }
        if (instruction.Operand is CilOperand.Entity target)
        {
            var directMethod = _methods.GetMethod(target.Key);
            return HasAllocatingInitializer(directMethod, null, allocating,
                       directInitializers, constructedInitializers) ||
                   _runtimeSafepoints.Classify(directMethod, _types) ||
                   directMethod.JSImport is not null ||
                   directIdentities.TryGetValue(target.Key, out var identity) &&
                   allocating.Contains(identity);
        }

        var method = ((CilOperand.MethodInstance)instruction.Operand).Value;
        return HasAllocatingInitializer(method.Definition, method.DeclaringType, allocating,
                   directInitializers, constructedInitializers) ||
               _runtimeSafepoints.Classify(method) ||
               method.Definition.JSImport is not null ||
               allocating.Contains(method.CanonicalName);
    }

    private static bool HasAllocatingInitializer(
        MethodDefinitionModel method,
        CliTypeIdentity? declaringType,
        HashSet<string> allocating,
        Dictionary<EntityKey, string> directInitializers,
        Dictionary<string, string> constructedInitializers)
    {
        if (!method.IsStatic || method.Name == ".cctor")
            return false;
        var initializer = declaringType is { Shape: CliTypeShape.GenericInstantiation }
            ? constructedInitializers.GetValueOrDefault(declaringType.CanonicalName)
            : directInitializers.GetValueOrDefault(method.DeclaringType);
        return initializer is not null && allocating.Contains(initializer);
    }
}
