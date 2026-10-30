using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;
using NetWasm.Compiler.Core.IntermediateRepresentation.Members;
using NetWasm.Compiler.Wasm.Emission.Planning;
using NetWasm.Compiler.Wasm.Emission.AddressTakenValues;
using NetWasm.Compiler.Wasm.Emission.CallLifetimes;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed record ValueFrameLayout(
    int Size,
    ImmutableDictionary<int, int> LocalOffsets,
    ImmutableDictionary<int, int> ArgumentOffsets,
    ImmutableDictionary<int, int> TemporaryOffsets,
    ImmutableHashSet<int> SpilledScalarLocals)
{
    public ScalarCallLifetimePlan ScalarCallLifetimes { get; init; } = ScalarCallLifetimePlan.Empty;

    public ImmutableDictionary<int, int> ScalarCallOffsets { get; init; } = [];

    public TransientAggregatePromotionPlan TransientAggregatePromotions { get; init; } =
        TransientAggregatePromotionPlan.Empty;

    public AddressTakenScalarPromotionPlan AddressTakenScalarPromotions { get; init; } =
        AddressTakenScalarPromotionPlan.Empty;

    public AddressTakenAggregatePromotionPlan AddressTakenAggregatePromotions
    { get; init; } = AddressTakenAggregatePromotionPlan.Empty;

    public ManagedByRefAggregatePromotionPlan ManagedByRefAggregatePromotions
    { get; init; } = ManagedByRefAggregatePromotionPlan.Empty;

    public ImmutableHashSet<int> DirectValueLoadOffsets { get; init; } = [];

    public ImmutableDictionary<(int InstructionOffset, int ParameterIndex), int>
        NativeArgumentOffsets
    { get; init; } =
            ImmutableDictionary<(int InstructionOffset, int ParameterIndex), int>.Empty;

    public ImmutableDictionary<int, int> MemberResultOffsets { get; init; } =
        ImmutableDictionary<int, int>.Empty;

    public ImmutableDictionary<int, int> CallReceiverOffsets { get; init; } =
        ImmutableDictionary<int, int>.Empty;
}

internal sealed class ValueFrameLayoutPlanner(
    IFieldRepository fields,
    IMethodRepository methods,
    ITargetLayout layouts,
    IValueLayoutProvider values,
    ICilTypeOperandResolver typeOperands,
    ICilTypeIdentityResolver typeIdentities,
    IArgumentTypeResolver argumentTypes,
    IArgumentSignatureTypeResolver argumentSignatureTypes,
    IDirectValueLoadSelector directValueLoads,
    ITransientAggregatePromotionPlanner transientAggregatePromotions) : IValueFrameLayoutPlanner
{
    private readonly IFieldRepository _fields =
        fields ?? throw new ArgumentNullException(nameof(fields));
    private readonly IMethodRepository _methods =
        methods ?? throw new ArgumentNullException(nameof(methods));
    private readonly ITargetLayout _layouts =
        layouts ?? throw new ArgumentNullException(nameof(layouts));
    private readonly IValueLayoutProvider _values =
        values ?? throw new ArgumentNullException(nameof(values));
    private readonly ICilTypeOperandResolver _typeOperands = typeOperands;
    private readonly ICilTypeIdentityResolver _typeIdentities = typeIdentities;
    private readonly IArgumentTypeResolver _argumentTypes = argumentTypes;
    private readonly IArgumentSignatureTypeResolver _argumentSignatureTypes =
        argumentSignatureTypes;
    private readonly IDirectValueLoadSelector _directValueLoads =
        directValueLoads ?? throw new ArgumentNullException(nameof(directValueLoads));
    private readonly ITransientAggregatePromotionPlanner _transientAggregatePromotions =
        transientAggregatePromotions ??
        throw new ArgumentNullException(nameof(transientAggregatePromotions));

    public ValueFrameLayout Create(StructuredMethod method, NativeImportPlan? nativeImports = null,
        MemberExecutionPlan? memberExecution = null,
        ManagedByRefMethodEffects? byRefEffects = null,
        AddressTakenScalarPromotionPlan? addressTakenScalarPromotions = null,
        AddressTakenAggregatePromotionPlan? addressTakenAggregatePromotions = null,
        ScalarCallLifetimePlan? scalarCallLifetimes = null)
    {
        ArgumentNullException.ThrowIfNull(method);

        var header = method.Header;
        var scalarCalls = scalarCallLifetimes ?? ScalarCallLifetimePlan.Empty;
        var preservedLocals = scalarCalls.CallLocals.Values.SelectMany(indices => indices)
            .Distinct().Order().ToImmutableArray();
        foreach (var local in preservedLocals)
        {
            if ((uint)local >= (uint)header.LocalSignatureTypes.Length ||
                header.LocalSignatureTypes[local].StackKind is not (CliValueKind.F4 or CliValueKind.F8 or CliValueKind.I8))
            {
                throw new InvalidOperationException("Scalar call preservation requires a supported numeric local.");
            }
        }
        var scalarPromotions = addressTakenScalarPromotions ??
            AddressTakenScalarPromotionPlan.Empty;
        var addressTakenAggregates = addressTakenAggregatePromotions ??
            AddressTakenAggregatePromotionPlan.Empty;
        var aggregatePromotions = _transientAggregatePromotions.Create(method);
        var directValueCandidates = _directValueLoads.Select(method);
        var directValueLoads = ImmutableHashSet.CreateBuilder<int>();
        var instructions = header.Instructions
            .Where(instruction => method.InstructionEntryStacks.ContainsKey(instruction.Offset))
            .ToImmutableArray();

        var size = instructions.Any(instruction =>
            instruction.Operation == CilOperation.LocalAllocate) ? 1 : 0;
        var locals = ImmutableDictionary.CreateBuilder<int, int>();
        var arguments = ImmutableDictionary.CreateBuilder<int, int>();
        var addressTakenArguments = instructions
            .Where(instruction => instruction.Operation == CilOperation.LoadArgumentAddress)
            .Select(CilOperandReader.GetIndex)
            .ToImmutableHashSet();
        foreach (var index in addressTakenArguments)
        {
            var type = _argumentSignatureTypes.Resolve(header, index);
            var kind = _argumentTypes.Resolve(header, index);
            if (kind is CliValueKind.ManagedAddress or CliValueKind.ValueType)
            {
                continue;
            }
            var alignment = _layouts.Target.GetStorageAlignment(type);
            size = Align(size, alignment);
            arguments.Add(index, size);
            size = checked(size + _layouts.Target.GetStorageSize(type));
        }

        var mutableValueArguments = instructions
            .Where(instruction => instruction.Operation is
                CilOperation.LoadArgumentAddress or CilOperation.StoreArgument)
            .Where(instruction => instruction.Operation == CilOperation.StoreArgument ||
                byRefEffects is null ||
                byRefEffects.RequiresOwnedStorage(CilOperandReader.GetIndex(instruction)))
            .Select(CilOperandReader.GetIndex)
            .Distinct()
            .Where(index => _argumentTypes.Resolve(header, index) == CliValueKind.ValueType);
        foreach (var index in mutableValueArguments)
        {
            var type = _argumentSignatureTypes.Resolve(header, index);
            var layout = _values.GetValueLayout(type);
            size = Align(size, layout.Alignment);
            arguments.Add(index, size);
            size = checked(size + layout.Size);
        }

        var addressTakenLocals = instructions
            .Where(instruction => instruction.Operation == CilOperation.LoadLocalAddress)
            .Select(CilOperandReader.GetIndex)
            .ToImmutableHashSet();
        var usedLocals = instructions
            .Where(instruction => instruction.Operation is
                CilOperation.LoadLocal or
                CilOperation.LoadLocalAddress or
                CilOperation.StoreLocal)
            .Select(CilOperandReader.GetIndex)
            .ToImmutableHashSet();
        var spilledScalars = ImmutableHashSet.CreateBuilder<int>();
        foreach ((var type, var index) in header.LocalSignatureTypes
                     .Select((type, index) => (type, index)))
        {
            if (!usedLocals.Contains(index))
            {
                continue;
            }
            if (aggregatePromotions.PromotedLocalIndices.Contains(index))
            {
                continue;
            }
            if (type.StackKind != CliValueKind.ValueType &&
                !addressTakenLocals.Contains(index))
            {
                continue;
            }
            var alignment = type.StackKind == CliValueKind.ValueType
                ? _values.GetValueLayout(type).Alignment
                : _layouts.Target.GetStorageAlignment(type);
            var storageSize = type.StackKind == CliValueKind.ValueType
                ? _values.GetValueLayout(type).Size
                : _layouts.Target.GetStorageSize(type);
            size = Align(size, alignment);
            locals.Add(index, size);
            size = checked(size + storageSize);
            if (type.StackKind != CliValueKind.ValueType &&
                !scalarPromotions.PromotedLocals.ContainsKey(index))
            {
                spilledScalars.Add(index);
            }
        }

        var temporaries = ImmutableDictionary.CreateBuilder<int, int>();
        var memberResults = ImmutableDictionary.CreateBuilder<int, int>();
        var callReceivers = ImmutableDictionary.CreateBuilder<int, int>();
        var nativeArguments = ImmutableDictionary.CreateBuilder<(int InstructionOffset, int ParameterIndex), int>();
        var stackRequirements = new Dictionary<int, StackStorageRequirement>();
        var stackAssignments = new Dictionary<int, int>();
        var transientReservations = new Dictionary<int, List<TransientStorageReservation>>();
        foreach (var instruction in instructions.Where(
                     instruction => instruction.Operation is CilOperation.NewObject or
                         CilOperation.NewRectangularArray or
                         CilOperation.NewBoundedRectangularArray or
                         CilOperation.UnboxAny or CilOperation.Call or
                         CilOperation.CallVirtual or CilOperation.LoadObject or
                         CilOperation.LoadArgument or CilOperation.LoadLocal or
                         CilOperation.LoadField or CilOperation.LoadStaticField or
                         CilOperation.LoadArrayElement or
                         CilOperation.LoadRectangularArrayElement or
                         CilOperation.CreateReadOnlySpanFromRva or
                         CilOperation.DefaultValue))
        {
            if (instruction.Operation is CilOperation.NewRectangularArray or
                CilOperation.NewBoundedRectangularArray)
            {
                var arrayType = _typeOperands.Resolve(instruction, header.MethodInstance);
                var dimensionsPerRank = instruction.Operation == CilOperation.NewBoundedRectangularArray
                    ? 2 : 1;
                ReserveTransient(
                    instruction.Offset,
                    TransientStorageKind.Temporary,
                    0,
                    checked(arrayType.ArrayRank * dimensionsPerRank * sizeof(int)),
                    sizeof(int));
                continue;
            }
            if (instruction.Operation == CilOperation.LoadArgument)
            {
                var index = CilOperandReader.GetIndex(instruction);
                var argumentType = _argumentSignatureTypes.Resolve(header, index);
                if (_argumentTypes.Resolve(header, index) == CliValueKind.ValueType)
                {
                    if (directValueCandidates.Contains(instruction.Offset))
                    {
                        directValueLoads.Add(instruction.Offset);
                    }
                    else
                    {
                        ReserveResult(argumentType, instruction, instruction.Operation);
                    }
                }
                continue;
            }
            if (instruction.Operation == CilOperation.LoadLocal)
            {
                if (aggregatePromotions.LocalTransferOffsets.Contains(instruction.Offset))
                {
                    continue;
                }
                var localType = header.LocalSignatureTypes[
                        CilOperandReader.GetIndex(instruction)];
                if (localType.StackKind == CliValueKind.ValueType)
                {
                    if (directValueCandidates.Contains(instruction.Offset))
                    {
                        directValueLoads.Add(instruction.Offset);
                    }
                    else
                    {
                        ReserveResult(localType, instruction, instruction.Operation);
                    }
                }
                continue;
            }
            if (instruction.Operation == CilOperation.DefaultValue)
            {
                var defaultType = _typeOperands.Resolve(instruction, header.MethodInstance);
                if (defaultType.StackKind == CliValueKind.ValueType)
                {
                    ReserveResult(defaultType, instruction, instruction.Operation);
                }
                continue;
            }
            if (instruction.Operation == CilOperation.CreateReadOnlySpanFromRva)
            {
                var span = instruction.Operand as CilOperand.ReadOnlySpanData ??
                    throw new InvalidOperationException(
                        "RVA span creation has no span data operand");
                ReserveResult(span.SpanType, instruction, instruction.Operation);
                continue;
            }
            if (instruction.Operation is CilOperation.LoadArrayElement or
                CilOperation.LoadRectangularArrayElement)
            {
                var operandType = _typeOperands.Resolve(instruction, header.MethodInstance);
                var elementType = instruction.Operation == CilOperation.LoadRectangularArrayElement
                    ? operandType.ElementType!
                    : operandType;
                if (elementType.StackKind == CliValueKind.ValueType)
                {
                    if (directValueCandidates.Contains(instruction.Offset))
                    {
                        directValueLoads.Add(instruction.Offset);
                    }
                    else
                    {
                        ReserveResult(elementType, instruction, instruction.Operation);
                    }
                }
                continue;
            }
            if (instruction.Operation == CilOperation.LoadObject)
            {
                var loadedType = _typeOperands.Resolve(instruction, header.MethodInstance);
                if (loadedType.StackKind == CliValueKind.ValueType)
                {
                    if (directValueCandidates.Contains(instruction.Offset))
                    {
                        directValueLoads.Add(instruction.Offset);
                    }
                    else
                    {
                        ReserveResult(loadedType, instruction, instruction.Operation);
                    }
                }
                continue;
            }
            if (instruction.Operation is CilOperation.LoadField or
                CilOperation.LoadStaticField)
            {
                var fieldType = instruction.Operand switch
                {
                    CilOperand.FieldInstance field => field.Value.FieldType,
                    CilOperand.Entity field => _fields.GetField(field.Key).SignatureType,
                    _ => throw new InvalidOperationException("field load has no field operand"),
                };
                if (fieldType.StackKind == CliValueKind.ValueType)
                {
                    ReserveResult(fieldType, instruction, instruction.Operation);
                }
                continue;
            }
            if (instruction.Operation is CilOperation.Call or CilOperation.CallVirtual)
            {
                if (aggregatePromotions.Producers.ContainsKey(instruction.Offset))
                {
                    continue;
                }
                ReserveCallResult(instruction);
                continue;
            }
            if (instruction.Operation == CilOperation.UnboxAny)
            {
                var unboxedType = _typeOperands.Resolve(instruction, header.MethodInstance);
                if (unboxedType.IsValueType)
                {
                    ReserveResult(unboxedType, instruction, instruction.Operation);
                }
                continue;
            }

            var constructor = instruction.Operand switch
            {
                CilOperand.MethodInstance methodTarget => methodTarget.Value.Definition,
                CilOperand.Entity entityTarget => _methods.GetMethod(entityTarget.Key),
                _ => throw new InvalidOperationException("constructor has no method operand"),
            };
            var declaringType = instruction.Operand is CilOperand.MethodInstance methodInstance
                ? methodInstance.Value.DeclaringType
                : _typeIdentities.Resolve(constructor.DeclaringType);
            if (declaringType.IsValueType)
            {
                ReserveUniqueResult(declaringType, instruction.Offset);
            }
        }

        foreach (var requirement in stackRequirements.OrderBy(requirement => requirement.Key))
        {
            size = Align(size, requirement.Value.Alignment);
            stackAssignments.Add(requirement.Key, size);
            size = checked(size + requirement.Value.Size);
        }
        foreach (var assignment in stackAssignments)
        {
            foreach (var instructionOffset in stackRequirements[assignment.Key].InstructionOffsets)
            {
                temporaries.Add(instructionOffset, assignment.Value);
            }
        }

        var transientLayouts = new Dictionary<int, ImmutableArray<TransientStoragePlacement>>();
        var transientAlignment = 1;
        var transientSize = 0;
        foreach (var reservation in transientReservations.OrderBy(reservation => reservation.Key))
        {
            var relativeSize = 0;
            var placements = ImmutableArray.CreateBuilder<TransientStoragePlacement>();
            foreach (var storage in reservation.Value)
            {
                relativeSize = Align(relativeSize, storage.Alignment);
                placements.Add(new(storage, relativeSize));
                relativeSize = checked(relativeSize + storage.Size);
                transientAlignment = Math.Max(transientAlignment, storage.Alignment);
            }
            transientSize = Math.Max(transientSize, relativeSize);
            transientLayouts.Add(reservation.Key, placements.ToImmutable());
        }
        if (transientSize != 0)
        {
            size = Align(size, transientAlignment);
            var transientBase = size;
            foreach (var layout in transientLayouts)
            {
                foreach (var placement in layout.Value)
                {
                    var offset = checked(transientBase + placement.RelativeOffset);
                    if (placement.Storage.Kind == TransientStorageKind.Temporary)
                    {
                        temporaries.Add(layout.Key, offset);
                    }
                    else if (placement.Storage.Kind == TransientStorageKind.NativeArgument)
                    {
                        nativeArguments.Add((layout.Key, placement.Storage.Index), offset);
                    }
                    else if (placement.Storage.Kind == TransientStorageKind.MemberResult)
                    {
                        memberResults.Add(layout.Key, offset);
                    }
                    else
                    {
                        callReceivers.Add(layout.Key, offset);
                    }
                }
            }
            size = checked(size + transientSize);
        }

        var scalarOffsets = ImmutableDictionary.CreateBuilder<int, int>();
        foreach (var local in preservedLocals)
        {
            var type = header.LocalSignatureTypes[local];
            size = Align(size, _layouts.Target.GetStorageAlignment(type));
            scalarOffsets.Add(local, size);
            size = checked(size + _layouts.Target.GetStorageSize(type));
        }

        return new ValueFrameLayout(
            Align(size, _layouts.Target.ObjectReferenceAlignment),
            locals.ToImmutable(),
            arguments.ToImmutable(),
            temporaries.ToImmutable(),
            spilledScalars.ToImmutable())
        {
            ScalarCallLifetimes = scalarCalls,
            ScalarCallOffsets = scalarOffsets.ToImmutable(),
            TransientAggregatePromotions = aggregatePromotions,
            AddressTakenScalarPromotions = scalarPromotions,
            AddressTakenAggregatePromotions = addressTakenAggregates,
            DirectValueLoadOffsets = directValueLoads.ToImmutable(),
            NativeArgumentOffsets = nativeArguments.ToImmutable(),
            MemberResultOffsets = memberResults.ToImmutable(),
            CallReceiverOffsets = callReceivers.ToImmutable(),
        };

        void ReserveCallResult(CilInstruction instruction)
        {
            var calledMethod = instruction.Operand switch
            {
                CilOperand.MethodInstance methodTarget => methodTarget.Value.Definition,
                CilOperand.Entity entityTarget => _methods.GetMethod(entityTarget.Key),
                _ => throw new InvalidOperationException("call has no method operand"),
            };
            var callSignature = instruction.Operand is CilOperand.MethodInstance instanceTarget
                ? instanceTarget.Value.Signature
                : calledMethod.Signature;
            if (memberExecution?.MethodInvokers.Contains(calledMethod.Key) == true)
            {
                var results = memberExecution.Methods.Values
                    .Select(method => method.Descriptor.Signature.ReturnSignatureType)
                    .Where(type => type.StackKind == CliValueKind.ValueType)
                    .Distinct().Select(_values.GetValueLayout).ToArray();
                if (results.Length != 0)
                {
                    // Only nullable scalars are admitted by member execution. Their return
                    // scratch is reused across dispatcher cases and contains no GC roots.
                    if (results.Any(layout => !layout.ReferenceOffsets.IsEmpty))
                    {
                        throw new CompilerException(new(DiagnosticCode.RuntimeContract,
                            "Member result scratch cannot contain managed references.",
                            calledMethod.Name, instruction.Offset));
                    }
                    ReserveTransient(
                        instruction.Offset,
                        TransientStorageKind.MemberResult,
                        0,
                        results.Max(layout => layout.Size),
                        results.Max(layout => layout.Alignment));
                }
                return;
            }
            if (memberExecution?.DelegateInvocation is { } delegateInvocation &&
                delegateInvocation.Invokers.Contains(calledMethod.Key))
            {
                var results = delegateInvocation.Methods.Values
                    .Select(method => method.Signature.ReturnSignatureType)
                    .Where(type => type.StackKind == CliValueKind.ValueType &&
                        type.Shape == CliTypeShape.GenericInstantiation)
                    .Distinct()
                    .Select(type => (Type: type, Underlying: type.TypeArguments[0]))
                    .Where(item => item.Type.ElementType?.FullName == "System.Nullable`1")
                    .Select(item => _values.GetValueLayout(item.Type))
                    .ToArray();
                if (results.Length != 0)
                {
                    if (results.Any(layout => !layout.ReferenceOffsets.IsEmpty))
                    {
                        throw new CompilerException(new(
                            DiagnosticCode.RuntimeContract,
                            "DynamicInvoke nullable result scratch cannot contain managed references.",
                            calledMethod.Name,
                            instruction.Offset));
                    }
                    ReserveTransient(
                        instruction.Offset,
                        TransientStorageKind.MemberResult,
                        0,
                        results.Max(layout => layout.Size),
                        results.Max(layout => layout.Alignment));
                }
                return;
            }
            if (calledMethod.NativeImport is not null)
            {
                if (nativeImports is null || !nativeImports.ByMethod.TryGetValue(calledMethod.Key, out var native))
                    throw new CompilerException(new(DiagnosticCode.CompilerInvariant,
                        "A native call has no published ABI plan.", calledMethod.Name, instruction.Offset));
                var lowering = native.Abi.Lowering;
                foreach (var parameter in lowering.Parameters)
                {
                    if (parameter.Value.Kind == NativeAbiValueKind.IndirectAggregate)
                        ReserveNativeArgument(instruction, parameter);
                }
                if (lowering.Result.LogicalType.StackKind == CliValueKind.ValueType)
                    ReserveNativeResult(instruction, lowering.Result);
                return;
            }
            var receiverType = instruction.Operand is CilOperand.MethodInstance receiver
                ? receiver.Value.DeclaringType
                : _typeIdentities.Resolve(calledMethod.DeclaringType);
            if (!calledMethod.IsStatic && receiverType.IsValueType &&
                receiverType.StackKind != CliValueKind.ValueType)
            {
                var receiverLayout = _values.GetValueLayout(receiverType);
                ReserveTransient(
                    instruction.Offset,
                    TransientStorageKind.CallReceiver,
                    0,
                    receiverLayout.Size,
                    receiverLayout.Alignment);
            }
            if (callSignature.ReturnSignatureType.StackKind == CliValueKind.ValueType)
            {
                // The managed ABI receives the return buffer together with pointers to
                // aggregate arguments. The result therefore cannot reuse the first
                // consumed evaluation-stack slot while the callee is still reading it.
                ReserveUniqueResult(
                    callSignature.ReturnSignatureType,
                    instruction.Offset);
            }
        }

        void ReserveUniqueResult(CliTypeIdentity type, int instructionOffset)
        {
            var layout = _values.GetValueLayout(type);
            size = Align(size, layout.Alignment);
            temporaries.Add(instructionOffset, size);
            size = checked(size + layout.Size);
        }

        void ReserveResult(
            CliTypeIdentity type,
            CilInstruction instruction,
            CilOperation operation)
        {
            var layout = _values.GetValueLayout(type);
            ReserveStackResult(instruction, operation, layout.Size, layout.Alignment);
        }

        void ReserveNativeArgument(
            CilInstruction instruction,
            NativeAbiParameterPlan parameter)
        {
            ValidateNativeStorage(parameter.Value);
            ReserveTransient(
                instruction.Offset,
                TransientStorageKind.NativeArgument,
                parameter.LogicalIndex,
                parameter.Value.Size,
                parameter.Value.Alignment);
        }

        void ReserveNativeResult(CilInstruction instruction, NativeAbiValuePlan value)
        {
            ValidateNativeStorage(value);
            ReserveStackResult(
                instruction,
                instruction.Operation,
                value.Size,
                value.Alignment);
        }

        void ValidateNativeStorage(NativeAbiValuePlan value)
        {
            if (value.Size <= 0 || value.Alignment <= 0 || value.Alignment > 16 ||
                (value.Alignment & (value.Alignment - 1)) != 0)
                throw new CompilerException(new(DiagnosticCode.CompilerInvariant,
                    "A native aggregate has invalid value-frame storage.", header.Method.Name));
        }

        void ReserveStackResult(
            CilInstruction instruction,
            CilOperation operation,
            int storageSize,
            int alignment)
        {
            var slot = GetResultStackSlot(instruction, operation);
            if (!stackRequirements.TryGetValue(slot, out var requirement))
            {
                requirement = new(storageSize, alignment, []);
                stackRequirements.Add(slot, requirement);
            }
            requirement = requirement with
            {
                Size = Math.Max(requirement.Size, storageSize),
                Alignment = Math.Max(requirement.Alignment, alignment),
                InstructionOffsets = requirement.InstructionOffsets.Add(instruction.Offset),
            };
            stackRequirements[slot] = requirement;
        }

        int GetResultStackSlot(CilInstruction instruction, CilOperation operation)
        {
            var entryCount = method.InstructionEntryStacks[instruction.Offset].Length;
            var consumed = operation switch
            {
                CilOperation.LoadArgument or CilOperation.LoadLocal or
                    CilOperation.LoadStaticField or CilOperation.DefaultValue or
                    CilOperation.CreateReadOnlySpanFromRva => 0,
                CilOperation.LoadObject or CilOperation.LoadField or
                    CilOperation.UnboxAny => 1,
                CilOperation.LoadArrayElement => 2,
                CilOperation.LoadRectangularArrayElement => checked(
                    _typeOperands.Resolve(instruction, header.MethodInstance).ArrayRank + 1),
                CilOperation.Call or CilOperation.CallVirtual => GetCallConsumed(instruction),
                _ => throw new InvalidOperationException(
                    $"Instruction '{operation}' does not produce a planned value temporary."),
            };
            var slot = checked(entryCount - consumed);
            if ((uint)slot >= (uint)header.MaxStack)
            {
                throw new CompilerException(new(
                    DiagnosticCode.CompilerInvariant,
                    "A value temporary has an invalid evaluation-stack result slot.",
                    header.Method.Name,
                    instruction.Offset));
            }
            return slot;
        }

        int GetCallConsumed(CilInstruction instruction)
        {
            var calledMethod = instruction.Operand switch
            {
                CilOperand.MethodInstance methodTarget => methodTarget.Value.Definition,
                CilOperand.Entity entityTarget => _methods.GetMethod(entityTarget.Key),
                _ => throw new InvalidOperationException("call has no method operand"),
            };
            var signature = instruction.Operand is CilOperand.MethodInstance instanceTarget
                ? instanceTarget.Value.Signature
                : calledMethod.Signature;
            return checked(signature.ParameterTypes.Length + (calledMethod.IsStatic ? 0 : 1));
        }

        void ReserveTransient(
            int instructionOffset,
            TransientStorageKind kind,
            int index,
            int storageSize,
            int alignment)
        {
            if (!transientReservations.TryGetValue(instructionOffset, out var reservations))
            {
                reservations = [];
                transientReservations.Add(instructionOffset, reservations);
            }
            reservations.Add(new(kind, index, storageSize, alignment));
        }
    }

    private sealed record StackStorageRequirement(
        int Size,
        int Alignment,
        ImmutableArray<int> InstructionOffsets);

    private enum TransientStorageKind
    {
        Temporary,
        NativeArgument,
        MemberResult,
        CallReceiver,
    }

    private readonly record struct TransientStorageReservation(
        TransientStorageKind Kind,
        int Index,
        int Size,
        int Alignment);

    private readonly record struct TransientStoragePlacement(
        TransientStorageReservation Storage,
        int RelativeOffset);

    private static int Align(int value, int alignment) =>
        checked((value + alignment - 1) & -alignment);
}
