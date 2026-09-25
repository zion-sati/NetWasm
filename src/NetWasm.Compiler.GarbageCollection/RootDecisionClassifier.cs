using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed class RootDecisionClassifier(
    ITypeRepository types,
    IFieldRepository fields,
    IMethodRepository methods,
    IReadOnlyDictionary<string, DispatchCallSiteModel>? dispatchCallSites = null) :
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
                instruction,
                request.AllocatingMethods,
                request.AllocatingConstructedMethods);
        }
        if (instruction.Operation is not (CilOperation.Call or CilOperation.CallVirtual))
            return false;
        var caller = request.Method.Method.CanonicalName;
        var dispatchKey = $"{caller}@{instruction.Offset:x8}";
        if (_dispatchCallSites.TryGetValue(dispatchKey, out var dispatch))
        {
            return dispatch.Targets.Any(target => target.Method.IsConstructed
                ? request.AllocatingConstructedMethods.Contains(target.Method.CanonicalName)
                : request.AllocatingMethods.Contains(target.Method.Definition.Key));
        }
        return instruction.Operand switch
        {
            CilOperand.Entity target =>
                IsAllocatingStaticCall(_methods.GetMethod(target.Key), null, request) ||
                _methods.GetMethod(target.Key).JSImport is not null ||
                request.AllocatingMethods.Contains(target.Key) &&
                _methods.GetMethod(target.Key).HasBody,
            CilOperand.MethodInstance target =>
                IsAllocatingStaticCall(target.Value.Definition, target.Value.DeclaringType, request) ||
                target.Value.Definition.JSImport is not null ||
                (target.Value.IsConstructed
                    ? request.AllocatingConstructedMethods.Contains(target.Value.CanonicalName)
                    : request.AllocatingMethods.Contains(target.Value.Definition.Key)) &&
                target.Value.Definition.HasBody,
            _ => false,
        };
    }

    private bool IsAllocatingStaticInitialization(
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
        return IsAllocatingInitializer(field.DeclaringType,
            (instruction.Operand as CilOperand.FieldInstance)?.Value.DeclaringType,
            allocatingMethods, allocatingConstructedMethods);
    }

    private bool IsAllocatingStaticCall(
        MethodDefinitionModel method,
        CliTypeIdentity? declaringType,
        RootDecisionRequest request) =>
        method.IsStatic && method.Name != ".cctor" &&
        IsAllocatingInitializer(method.DeclaringType, declaringType,
            request.AllocatingMethods, request.AllocatingConstructedMethods);

    private bool IsAllocatingInitializer(
        EntityKey typeDefinition,
        CliTypeIdentity? declaringType,
        ISet<EntityKey> allocatingMethods,
        ISet<string> allocatingConstructedMethods)
    {
        var initializer = _types.GetTypeDefinition(typeDefinition)
            .Methods.Select(_methods.GetMethod)
            .SingleOrDefault(method => method.Name == ".cctor");
        if (initializer is null)
            return false;
        // Allocation capabilities include the failure boundary of every
        // reachable initializer, even if its managed body does not allocate.
        if (declaringType is { Shape: CliTypeShape.GenericInstantiation })
        {
            var canonicalName =
                $"{declaringType.CanonicalName}::" +
                $"0x{initializer.Key.MetadataToken:x8}";
            return allocatingConstructedMethods.Contains(canonicalName);
        }
        return allocatingMethods.Contains(initializer.Key);
    }
}
