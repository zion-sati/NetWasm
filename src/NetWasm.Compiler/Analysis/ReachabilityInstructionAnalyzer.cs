using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.IntermediateRepresentation.Calls;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class ReachabilityInstructionAnalyzer(
    ITypeFinder typeFinder,
    ITypeIdentityResolver typeIdentities,
    ICalledMethodResolver calledMethods,
    ITypeOperandResolver typeOperands,
    IDispatchSiteKeyBuilder dispatchSiteKeys,
    IDelegateMethodClassifier delegateMethods,
    INullableTypeResolver nullableTypes,
    IManagedCallSiteFactory managedCallSites,
    INativeCallbackDeclarationValidator nativeCallbacks,
    IDispatchReceiverBoundResolver receiverBounds,
    IRuntimeIntrinsicRegistry intrinsics) : IReachabilityInstructionAnalyzer
{
    public ReachabilityInstructionAnalysis Analyze(ReachabilityInstructionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var runtimeTypes = ImmutableArray.CreateBuilder<CliTypeIdentity>();
        var constructedTypes = ImmutableArray.CreateBuilder<CliTypeIdentity>();
        var allocatedTypes = ImmutableArray.CreateBuilder<CliTypeIdentity>();
        var types = ImmutableArray.CreateBuilder<EntityKey>();
        var strings = ImmutableArray.CreateBuilder<string>();
        var methods = ImmutableArray.CreateBuilder<ReachabilityMethodReference>();
        var entities = ImmutableArray.CreateBuilder<ReachabilityEntityReference>();
        var fields = ImmutableArray.CreateBuilder<FieldInstanceModel>();
        var methodDescriptors = ImmutableArray.CreateBuilder<MethodInstanceModel>();
        var fieldDescriptors = ImmutableArray.CreateBuilder<FieldInstanceModel>();
        var dispatches = ImmutableArray.CreateBuilder<ReachabilityDispatch>();
        var callableMethods = ImmutableArray.CreateBuilder<MethodInstanceModel>();
        var reachedNativeCallbacks = ImmutableArray.CreateBuilder<MethodInstanceModel>();
        var callSites = ImmutableArray.CreateBuilder<ManagedCallSite>();
        var requiresTypeFacts = false;
        var requiresDelegateInvoke = false;
        var requiresGenericArguments = false;
        var requiresMemberNames = false;
        var instructions = request.Body.Instructions;
        for (var instructionIndex = 0; instructionIndex < instructions.Length; instructionIndex++)
        {
            var instruction = instructions[instructionIndex];
            if (instruction.Operation == CilOperation.Constrained &&
                instruction.Operand is not CilOperand.TypeIdentity)
            {
                throw new InvalidOperationException(
                    "A constrained instruction requires a type identity operand.");
            }

            var calledMethod = calledMethods.Resolve(instruction);
            if (calledMethod is not null)
            {
                requiresMemberNames |= ReachabilityMarkers.RequiresMemberNames(calledMethod);
                requiresDelegateInvoke |= ReachabilityMarkers.RequiresDelegateInvoke(calledMethod);
                requiresGenericArguments |= ReachabilityMarkers.RequiresGenericArguments(calledMethod);
                callSites.Add(managedCallSites.Create(
                    request.Method,
                    request.Body,
                    instructionIndex,
                    calledMethod,
                    typeOperands)
                    );
            }
            if (instruction.Operation == CilOperation.NewObject && calledMethod is not null)
            {
                if (!calledMethod.DeclaringType.IsValueType)
                {
                    allocatedTypes.Add(calledMethod.DeclaringType);
                }

                if (delegateMethods.Classify(calledMethod) == DelegateMethodKind.Constructor)
                {
                    types.Add(calledMethod.Definition.DeclaringType);
                    constructedTypes.Add(calledMethod.DeclaringType);
                    continue;
                }
            }
            if (instruction.Operation == CilOperation.Box &&
                instruction.Operand is CilOperand.TypeIdentity boxedType)
            {
                var nullableUnderlyingType = nullableTypes.Resolve(boxedType.Value);
                if (nullableUnderlyingType is not null)
                {
                    runtimeTypes.Add(nullableUnderlyingType);
                }
                allocatedTypes.Add(nullableUnderlyingType ?? boxedType.Value);
            }
            if (instruction.Operation == CilOperation.NewArray)
            {
                var elementType = typeOperands.Resolve(instruction, request.Method);
                var arrayType = CliTypeIdentity.SzArray(elementType);
                runtimeTypes.Add(elementType);
                allocatedTypes.Add(arrayType);
                constructedTypes.Add(arrayType);
            }
            if (instruction.Operation is CilOperation.NewRectangularArray or
                CilOperation.NewBoundedRectangularArray)
            {
                var arrayType = typeOperands.Resolve(instruction, request.Method);
                runtimeTypes.Add(arrayType.ElementType!);
                allocatedTypes.Add(arrayType);
                constructedTypes.Add(arrayType);
            }
            if (instruction.Operation is CilOperation.MaterializeType or CilOperation.GetObjectType)
            {
                var typeFacade = typeFinder.FindType("System.Type");
                types.Add(typeFacade.Key);
                allocatedTypes.Add(typeIdentities.GetTypeIdentity(typeFacade.Key));
                if (instruction.Operation == CilOperation.GetObjectType &&
                    instruction.Operand is CilOperand.TypeIdentity constrainedType)
                {
                    runtimeTypes.Add(constrainedType.Value);
                }
                if (CilTypeMaterialization.IsExactObjectType(instruction))
                {
                    var exactType = typeOperands.Resolve(instruction, request.Method);
                    runtimeTypes.Add(exactType);
                    constructedTypes.Add(exactType);
                }
            }
            if (instruction.Operation == CilOperation.GetTypeFacts)
            {
                requiresTypeFacts = true;
            }
            if (instruction.Operation == CilOperation.LoadTypeToken)
            {
                var runtimeType = typeOperands.Resolve(instruction, request.Method);
                runtimeTypes.Add(runtimeType);
                constructedTypes.Add(runtimeType);
                continue;
            }
            if (instruction.Operation == CilOperation.LoadMethodToken)
            {
                var descriptor = instruction.Operand is CilOperand.MethodInstance method
                    ? method.Value
                    : throw new InvalidOperationException(
                        "A method-token instruction requires a method instance operand.");
                methodDescriptors.Add(descriptor);
                continue;
            }
            if (instruction.Operation == CilOperation.LoadFieldToken)
            {
                var descriptor = instruction.Operand switch
                {
                    CilOperand.FieldInstance field => field.Value,
                    _ => throw new InvalidOperationException(
                        "A field-token instruction requires a field operand."),
                };
                fieldDescriptors.Add(descriptor);
                continue;
            }
            if (instruction.Operation == CilOperation.InitializeArrayData)
            {
                // The initializer's bytes are copied from the field's static data.
                fields.Add(((CilOperand.ArrayInitializerData)instruction.Operand).Field);
                continue;
            }
            if (instruction.Operation == CilOperation.CreateReadOnlySpanFromRva)
            {
                var span = instruction.Operand as CilOperand.ReadOnlySpanData ??
                    throw new InvalidOperationException(
                        "An RVA span instruction requires span data metadata.");
                fields.Add(span.Field);
                constructedTypes.Add(span.SpanType);
                constructedTypes.Add(span.ElementType);
                continue;
            }
            if (instruction.Operation == CilOperation.LoadStaticDataElement)
            {
                var data = instruction.Operand as CilOperand.ReadOnlySpanData ??
                    throw new InvalidOperationException(
                        "A static data element instruction requires span data metadata.");
                fields.Add(data.Field);
                constructedTypes.Add(data.ElementType);
                continue;
            }
            // A delegate's Invoke is the runtime's: its type's invoke helper, which a call, direct or
            // virtual, runs and which its address names. A virtual address load keeps its dispatch
            // site, whose target is the same helper.
            if (calledMethod is not null &&
                instruction.Operation is CilOperation.Call or CilOperation.CallVirtual or CilOperation.LoadFunction &&
                delegateMethods.Classify(calledMethod) == DelegateMethodKind.Invoke)
            {
                types.Add(calledMethod.Definition.DeclaringType);
                if (instruction.Operation == CilOperation.LoadFunction)
                {
                    callableMethods.Add(calledMethod);
                }
                continue;
            }
            if (instruction.Operation == CilOperation.LoadFunction && calledMethod is not null)
            {
                if (calledMethod.Definition.NativeCallback is not null)
                {
                    nativeCallbacks.ValidateAddressTarget(calledMethod);
                    reachedNativeCallbacks.Add(calledMethod);
                    methods.Add(new(CilOperation.Call, calledMethod));
                    continue;
                }
                callableMethods.Add(calledMethod);
                methods.Add(new(CilOperation.Call, calledMethod));
                continue;
            }
            if (calledMethod?.Definition.NativeCallback is not null)
            {
                throw new CompilerException(new(
                    DiagnosticCode.NativeInterop,
                    "An UnmanagedCallersOnly method cannot be invoked directly from managed code.",
                    calledMethod.CanonicalName));
            }
            var precedingConstraintType = instructionIndex > 0 &&
                instructions[instructionIndex - 1].Operation == CilOperation.Constrained
                    ? ((CilOperand.TypeIdentity)instructions[instructionIndex - 1].Operand).Value
                    : null;
            // A call that names a method whose behaviour depends on its receiver's exact type,
            // without a constrained prefix that names it (a struct's base.Equals, or the body a
            // delegate to ValueType.Equals runs), dispatches on the receiver like a virtual call,
            // but always to the named method (ECMA-335 III.3.19).
            var receiverTyped = instruction.Operation == CilOperation.Call && calledMethod is not null &&
                precedingConstraintType is null &&
                intrinsics.TryGetIntrinsic(calledMethod.Definition.Key, out var intrinsic) &&
                RuntimeIntrinsicKinds.DependsOnReceiverType(intrinsic);
            if (calledMethod is not null &&
                (receiverTyped ||
                    instruction.Operation is CilOperation.CallVirtual or CilOperation.LoadVirtualFunction &&
                    calledMethod.Definition.IsVirtual &&
                    (!calledMethod.DeclaringType.IsValueType ||
                        instruction.Operation == CilOperation.LoadVirtualFunction) &&
                    precedingConstraintType?.IsValueType is not true))
            {
                var declaration = new DispatchDeclaration(
                    request.Method.CanonicalName,
                    instruction.Offset,
                    calledMethod,
                    instruction.Operation,
                    receiverBounds.Resolve(request, instructionIndex, calledMethod));
                dispatches.Add(new(
                    dispatchSiteKeys.Build(request.Method.CanonicalName, instruction.Offset),
                    declaration));
                continue;
            }
            if (instruction.Operation is
                CilOperation.CastClass or
                CilOperation.IsInstance or
                CilOperation.UnboxAny)
            {
                var castType = typeOperands.Resolve(instruction, request.Method);
                var nullableUnderlyingType = nullableTypes.Resolve(castType);
                if (instruction.Operation != CilOperation.UnboxAny ||
                    !castType.IsValueType ||
                    nullableUnderlyingType is not null)
                {
                    var membershipType = nullableUnderlyingType ?? castType;
                    runtimeTypes.Add(membershipType);
                    constructedTypes.Add(membershipType);
                    if (castType.Shape is CliTypeShape.SzArray or CliTypeShape.Array)
                    {
                        allocatedTypes.Add(castType);
                    }
                }
            }

            if (instruction.Operand is CilOperand.UserString literal)
            {
                strings.Add(literal.Value);
                var stringType = typeFinder.FindType("System.String");
                types.Add(stringType.Key);
                allocatedTypes.Add(typeIdentities.GetTypeIdentity(stringType.Key));
            }
            else if (instruction.Operand is CilOperand.Entity entity)
            {
                entities.Add(new(instruction.Operation, entity.Key));
            }
            else if (instruction.Operand is CilOperand.MethodInstance target)
            {
                methods.Add(new(instruction.Operation, target.Value));
            }
            else if (instruction.Operand is CilOperand.FieldInstance field)
            {
                fields.Add(field.Value);
            }
            else if (instruction.Operand is CilOperand.TypeIdentity type)
            {
                var runtimeType = instruction.Operation is
                        CilOperation.CastClass or
                        CilOperation.IsInstance or
                        CilOperation.UnboxAny
                    ? nullableTypes.Resolve(type.Value) ?? type.Value
                    : type.Value;
                runtimeTypes.Add(runtimeType);
                constructedTypes.Add(runtimeType);
                if (instruction.Operation is (CilOperation.CastClass or CilOperation.IsInstance) &&
                    type.Value.Shape is (CliTypeShape.SzArray or CliTypeShape.Array))
                {
                    allocatedTypes.Add(type.Value);
                }
            }
        }
        return new(
            runtimeTypes.ToImmutable(),
            constructedTypes.ToImmutable(),
            allocatedTypes.ToImmutable(),
            types.ToImmutable(),
            strings.ToImmutable(),
            methods.ToImmutable(),
            entities.ToImmutable(),
            fields.ToImmutable(),
            dispatches.ToImmutable(),
            callableMethods.ToImmutable(),
            callSites.ToImmutable())
        {
            MethodDescriptors = methodDescriptors.ToImmutable(),
            FieldDescriptors = fieldDescriptors.ToImmutable(),
            RequiresTypeFacts = requiresTypeFacts,
            RequiresDelegateInvoke = requiresDelegateInvoke,
            RequiresGenericArguments = requiresGenericArguments,
            RequiresMemberNames = requiresMemberNames,
            NativeCallbacks = reachedNativeCallbacks.ToImmutable(),
        };

    }
}
