using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.GarbageCollection;

public sealed class CalleeArgumentRootingClassifier(
    ITypeRepository types,
    IMethodRepository methods,
    IReadOnlyDictionary<string, DispatchCallSiteModel>? dispatchCallSites,
    IRuntimeAllocationSafepointClassifier runtimeSafepoints,
    ITypeClassifier typeClassifier,
    IMethodInitializationTriggerClassifier initializationTriggers) :
    ICalleeArgumentRootingClassifier
{
    private readonly ITypeRepository _types =
        types ?? throw new ArgumentNullException(nameof(types));
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

    // A compiled callee roots each parameter it keeps live across its own
    // safepoints, as the single-cast delegate helper relies on, so a call need
    // not root the references it hands over. Arguments stay rooted where the
    // call site itself may collect first (a type initializer, an import's
    // marshalling, a runtime helper or a constrained receiver) or where the
    // callee is not a compiled body.
    public int Classify(RootDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var instruction = request.Instruction;
        if (instruction.Operation is not (CilOperation.Call or CilOperation.CallVirtual) ||
            IsConstrained(request))
        {
            return 0;
        }
        var (declared, instance) = instruction.Operand switch
        {
            CilOperand.Entity entity => (_methods.GetMethod(entity.Key), (MethodInstanceModel?)null),
            CilOperand.MethodInstance call => (call.Value.Definition, call.Value),
            _ => (null, null),
        };
        if (declared is null ||
            declared is { IsStatic: false, Name: "Invoke" } &&
            _typeClassifier.IsDelegateType(declared.DeclaringType) ||
            (instance is null
                ? _runtimeSafepoints.Classify(declared, _types)
                : _runtimeSafepoints.Classify(instance)))
        {
            return 0;
        }
        var dispatchKey = $"{request.Method.Method.CanonicalName}@{instruction.Offset:x8}";
        var calleeRoots = _dispatchCallSites.TryGetValue(dispatchKey, out var dispatch)
            ? dispatch.Targets.All(target => RootsItsArguments(target.Method.Definition, target.Method, request))
            : RootsItsArguments(declared, instance, request);
        return calleeRoots
            ? (instance?.Signature ?? declared.Signature).ParameterTypes.Length + (declared.IsStatic ? 0 : 1)
            : 0;
    }

    private bool RootsItsArguments(
        MethodDefinitionModel method,
        MethodInstanceModel? instance,
        RootDecisionRequest request) =>
        method is
        {
            HasBody: true, UnsafeAccessor: null, IsDelegateAsynchronousCall: false,
            JSImport: null, NativeImport: null, WitImport: null
        } &&
        !(instance is null ? _runtimeSafepoints.Classify(method, _types) : _runtimeSafepoints.Classify(instance)) &&
        !(_initializationTriggers.Classify(method) &&
            TypeInitializerAllocation.IsAllocating(_types, _methods, method.DeclaringType, instance?.DeclaringType,
                request.AllocatingMethods, request.AllocatingConstructedMethods));

    private static bool IsConstrained(RootDecisionRequest request)
    {
        var instructions = request.Method.ControlFlow.Graph.Blocks
            .First(block => block.Index == request.Block).Instructions;
        var index = instructions.IndexOf(request.Instruction);
        return index > 0 && instructions[index - 1].Operation == CilOperation.Constrained;
    }
}
