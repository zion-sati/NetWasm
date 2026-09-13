using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.ControlFlow.Structured;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission;

internal sealed record ValueFrameLayout(
    int Size,
    ImmutableDictionary<int, int> LocalOffsets,
    ImmutableDictionary<int, int> ArgumentOffsets,
    ImmutableDictionary<int, int> TemporaryOffsets,
    ImmutableHashSet<int> SpilledScalarLocals);

internal sealed class ValueFrameLayoutPlanner(
    IFieldRepository fields,
    IMethodRepository methods,
    ITargetLayout layouts,
    IValueLayoutProvider values,
    ICilTypeOperandResolver typeOperands,
    ICilTypeIdentityResolver typeIdentities,
    IArgumentTypeResolver argumentTypes,
    IArgumentSignatureTypeResolver argumentSignatureTypes) : IValueFrameLayoutPlanner
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

    public ValueFrameLayout Create(StructuredMethodHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        var size = header.Instructions.Any(instruction =>
            instruction.Operation == CilOperation.LocalAllocate) ? 1 : 0;
        var locals = ImmutableDictionary.CreateBuilder<int, int>();
        var arguments = ImmutableDictionary.CreateBuilder<int, int>();
        var addressTakenArguments = header.Instructions
            .Where(instruction => instruction.Operation == CilOperation.LoadArgumentAddress)
            .Select(CilOperandReader.GetIndex)
            .ToImmutableHashSet();
        foreach (var index in addressTakenArguments)
        {
            var type = _argumentSignatureTypes.Resolve(header, index);
            if (type.StackKind is CliValueKind.ManagedAddress or CliValueKind.ValueType)
            {
                continue;
            }
            var alignment = _layouts.Target.GetStorageAlignment(type);
            size = Align(size, alignment);
            arguments.Add(index, size);
            size = checked(size + _layouts.Target.GetStorageSize(type));
        }

        var addressTakenLocals = header.Instructions
            .Where(instruction => instruction.Operation == CilOperation.LoadLocalAddress)
            .Select(CilOperandReader.GetIndex)
            .ToImmutableHashSet();
        var spilledScalars = ImmutableHashSet.CreateBuilder<int>();
        foreach ((var type, var index) in header.LocalSignatureTypes
                     .Select((type, index) => (type, index)))
        {
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
            if (type.StackKind != CliValueKind.ValueType)
            {
                spilledScalars.Add(index);
            }
        }

        var temporaries = ImmutableDictionary.CreateBuilder<int, int>();
        foreach (var instruction in header.Instructions.Where(
                     instruction => instruction.Operation is CilOperation.NewObject or
                         CilOperation.NewRectangularArray or
                         CilOperation.UnboxAny or CilOperation.Call or
                         CilOperation.CallVirtual or CilOperation.LoadObject or
                         CilOperation.LoadArgument or CilOperation.LoadLocal or
                         CilOperation.LoadField or CilOperation.LoadStaticField or
                         CilOperation.LoadArrayElement or CilOperation.DefaultValue))
        {
            if (instruction.Operation == CilOperation.NewRectangularArray)
            {
                var arrayType = _typeOperands.Resolve(instruction, header.MethodInstance);
                size = Align(size, sizeof(int));
                temporaries.Add(instruction.Offset, size);
                size = checked(size + arrayType.ArrayRank * sizeof(int));
                continue;
            }
            if (instruction.Operation == CilOperation.LoadArgument)
            {
                var index = CilOperandReader.GetIndex(instruction);
                var argumentType = _argumentSignatureTypes.Resolve(header, index);
                if (_argumentTypes.Resolve(header, index) == CliValueKind.ValueType)
                {
                    Reserve(argumentType, instruction.Offset);
                }
                continue;
            }
            if (instruction.Operation == CilOperation.LoadLocal)
            {
                var localType = header.LocalSignatureTypes[
                        CilOperandReader.GetIndex(instruction)];
                if (localType.StackKind == CliValueKind.ValueType)
                {
                    Reserve(localType, instruction.Offset);
                }
                continue;
            }
            if (instruction.Operation == CilOperation.DefaultValue)
            {
                var defaultType = _typeOperands.Resolve(instruction, header.MethodInstance);
                if (defaultType.StackKind == CliValueKind.ValueType)
                {
                    Reserve(defaultType, instruction.Offset);
                }
                continue;
            }
            if (instruction.Operation == CilOperation.LoadArrayElement)
            {
                var elementType = _typeOperands.Resolve(instruction, header.MethodInstance);
                if (elementType.StackKind == CliValueKind.ValueType)
                {
                    Reserve(elementType, instruction.Offset);
                }
                continue;
            }
            if (instruction.Operation == CilOperation.LoadObject)
            {
                var loadedType = _typeOperands.Resolve(instruction, header.MethodInstance);
                if (loadedType.StackKind == CliValueKind.ValueType)
                {
                    Reserve(loadedType, instruction.Offset);
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
                    Reserve(fieldType, instruction.Offset);
                }
                continue;
            }
            if (instruction.Operation is CilOperation.Call or CilOperation.CallVirtual)
            {
                ReserveCallResult(instruction);
                continue;
            }
            if (instruction.Operation == CilOperation.UnboxAny)
            {
                var unboxedType = _typeOperands.Resolve(instruction, header.MethodInstance);
                if (unboxedType.IsValueType)
                {
                    Reserve(unboxedType, instruction.Offset);
                }
                continue;
            }

            var constructor = instruction.Operand switch
            {
                CilOperand.MethodInstance method => method.Value.Definition,
                CilOperand.Entity method => _methods.GetMethod(method.Key),
                _ => throw new InvalidOperationException("constructor has no method operand"),
            };
            var declaringType = instruction.Operand is CilOperand.MethodInstance methodInstance
                ? methodInstance.Value.DeclaringType
                : _typeIdentities.Resolve(constructor.DeclaringType);
            if (declaringType.IsValueType)
            {
                Reserve(declaringType, instruction.Offset);
            }
        }

        return new ValueFrameLayout(
            Align(size, _layouts.Target.ObjectReferenceAlignment),
            locals.ToImmutable(),
            arguments.ToImmutable(),
            temporaries.ToImmutable(),
            spilledScalars.ToImmutable());

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
            var receiverType = instruction.Operand is CilOperand.MethodInstance receiver
                ? receiver.Value.DeclaringType
                : _typeIdentities.Resolve(calledMethod.DeclaringType);
            if (!calledMethod.IsStatic && receiverType.IsValueType &&
                receiverType.StackKind != CliValueKind.ValueType)
            {
                Reserve(receiverType, instruction.Offset);
                return;
            }
            if (callSignature.ReturnSignatureType.StackKind == CliValueKind.ValueType)
            {
                Reserve(callSignature.ReturnSignatureType, instruction.Offset);
            }
        }

        void Reserve(CliTypeIdentity type, int instructionOffset)
        {
            var layout = _values.GetValueLayout(type);
            size = Align(size, layout.Alignment);
            temporaries.Add(instructionOffset, size);
            size = checked(size + layout.Size);
        }
    }

    private static int Align(int value, int alignment) =>
        checked((value + alignment - 1) & -alignment);
}
