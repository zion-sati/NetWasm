using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.GarbageCollection;

public sealed class RootDecisionClassifier(
    ITypeRepository types,
    IFieldRepository fields,
    IMethodRepository methods,
    IReadOnlyDictionary<string, DispatchCallSiteModel>? dispatchCallSites,
    IRuntimeAllocationSafepointClassifier runtimeSafepoints,
    ITypeClassifier typeClassifier,
    IMethodInitializationTriggerClassifier initializationTriggers,
    IStaticFieldInitializationClassifier initializedFields) :
    IRootDecisionClassifier
{
    private readonly ITypeRepository _types =
        types ?? throw new ArgumentNullException(nameof(types));
    private readonly IFieldRepository _fields =
        fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IMethodRepository _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly IReadOnlyDictionary<string, DispatchCallSiteModel> _dispatchCallSites =
        dispatchCallSites ?? ImmutableDictionary<string, DispatchCallSiteModel>.Empty;
    private readonly IRuntimeAllocationSafepointClassifier _runtimeSafepoints =
        runtimeSafepoints ?? throw new ArgumentNullException(nameof(runtimeSafepoints));

    private readonly ITypeClassifier _typeClassifier = typeClassifier ??
        throw new ArgumentNullException(nameof(typeClassifier));
    private readonly IMethodInitializationTriggerClassifier _initializationTriggers = initializationTriggers ??
        throw new ArgumentNullException(nameof(initializationTriggers));
    private readonly IStaticFieldInitializationClassifier _initializedFields = initializedFields ??
        throw new ArgumentNullException(nameof(initializedFields));

    public bool Decide(RootDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Method);
        ArgumentNullException.ThrowIfNull(request.Instruction);
        ArgumentNullException.ThrowIfNull(request.AllocatingMethods);
        ArgumentNullException.ThrowIfNull(request.AllocatingConstructedMethods);
        var instruction = request.Instruction;
        if (CilSafepointClassifier.RequiresUnconditionalRootDecision(instruction) ||
            CilSafepointClassifier.MayTransferControlExceptionally(instruction) &&
            !request.Method.ControlFlow.Graph.ExceptionalSuccessors[request.Block].IsEmpty)
        {
            return true;
        }
        if (instruction.Operation is CilOperation.LoadStaticField or
            CilOperation.LoadStaticFieldAddress or CilOperation.StoreStaticField)
        {
            return IsAllocatingStaticInitialization(
                request.Method,
                instruction,
                request.AllocatingMethods,
                request.AllocatingConstructedMethods);
        }
        if (instruction.Operation is not (CilOperation.Call or CilOperation.CallVirtual))
            return false;
        var calledMethod = instruction.Operand switch
        {
            CilOperand.Entity entityCall => _methods.GetMethod(entityCall.Key),
            CilOperand.MethodInstance instanceCall => instanceCall.Value.Definition,
            _ => null,
        };
        // Delegate helpers may invoke any reachable target, including one that
        // collects. Their declaration has no managed body or ordinary dispatch
        // target whose allocation capability can establish this boundary.
        if (calledMethod is { IsStatic: false, Name: "Invoke" } &&
            _typeClassifier.IsDelegateType(calledMethod.DeclaringType))
        {
            return true;
        }
        // Keep the publication boundary aligned with intrinsic-first emission
        // and allocation propagation, including bodyless dispatch targets.
        var runtimeSafepoint = instruction.Operand switch
        {
            CilOperand.Entity => _runtimeSafepoints.Classify(calledMethod!, _types),
            CilOperand.MethodInstance instance => _runtimeSafepoints.Classify(instance.Value),
            _ => false,
        };
        if (runtimeSafepoint)
            return true;
        var caller = request.Method.Method.CanonicalName;
        var dispatchKey = $"{caller}@{instruction.Offset:x8}";
        if (_dispatchCallSites.TryGetValue(dispatchKey, out var dispatch))
        {
            return dispatch.Targets.Any(target =>
                IsAllocatingMethodInitialization(target.Method.Definition, target.Method.DeclaringType, request) ||
                _runtimeSafepoints.Classify(target.Method) ||
                (target.Method.IsConstructed
                    ? request.AllocatingConstructedMethods.Contains(target.Method.CanonicalName)
                    : request.AllocatingMethods.Contains(target.Method.Definition.Key)));
        }
        return instruction.Operand switch
        {
            CilOperand.Entity target =>
                IsAllocatingMethodInitialization(_methods.GetMethod(target.Key), null, request) ||
                _methods.GetMethod(target.Key).JSImport is not null ||
                _methods.GetMethod(target.Key).NativeImport is not null ||
                request.AllocatingMethods.Contains(target.Key) &&
                _methods.GetMethod(target.Key).HasManagedBody,
            CilOperand.MethodInstance target =>
                IsAllocatingMethodInitialization(target.Value.Definition, target.Value.DeclaringType, request) ||
                target.Value.Definition.JSImport is not null ||
                target.Value.Definition.NativeImport is not null ||
                (target.Value.IsConstructed
                    ? request.AllocatingConstructedMethods.Contains(target.Value.CanonicalName)
                    : request.AllocatingMethods.Contains(target.Value.Definition.Key)) &&
                target.Value.Definition.HasManagedBody,
            _ => false,
        };
    }

    private bool IsAllocatingStaticInitialization(
        ManagedMethodBody accessor,
        CilInstruction instruction,
        ISet<EntityKey> allocatingMethods,
        ISet<string> allocatingConstructedMethods)
    {
        var field = instruction.Operand switch
        {
            CilOperand.FieldInstance instance => instance.Value.Definition,
            CilOperand.Entity entity => _fields.GetField(entity.Key),
            _ => null,
        };
        if (field is null)
            return false;
        var declaringType = (instruction.Operand as CilOperand.FieldInstance)?.Value.DeclaringType;
        // An access with no initialization check cannot run the initializer.
        return !_initializedFields.IsInitializedFor(accessor.Method.Definition, accessor.Method.DeclaringType,
                accessor.Body.Instructions, instruction) &&
            TypeInitializerAllocation.IsAllocating(_types, _methods, field.DeclaringType, declaringType,
                allocatingMethods, allocatingConstructedMethods);
    }

    private bool IsAllocatingMethodInitialization(
        MethodDefinitionModel method,
        CliTypeIdentity? declaringType,
        RootDecisionRequest request) =>
        _initializationTriggers.Classify(method) &&
        TypeInitializerAllocation.IsAllocating(_types, _methods, method.DeclaringType, declaringType,
            request.AllocatingMethods, request.AllocatingConstructedMethods);
}
