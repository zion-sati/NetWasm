using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.LoopInvariantFields;

internal sealed class FieldProjectionResolver(
    ICalleeMethodBodyReader methodBodies,
    ICalledMethodResolver calledMethods,
    IFieldRepository fields,
    ITypeDefinitionResolver typeDefinitions) : IFieldProjectionResolver
{
    private readonly ICalleeMethodBodyReader _methodBodies = methodBodies ??
        throw new ArgumentNullException(nameof(methodBodies));
    private readonly ICalledMethodResolver _calledMethods = calledMethods ??
        throw new ArgumentNullException(nameof(calledMethods));
    private readonly IFieldRepository _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));

    public FieldProjection? Resolve(
        CliTypeIdentity aggregateType,
        CilInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(aggregateType);
        ArgumentNullException.ThrowIfNull(instruction);
        if (aggregateType.StackKind != CliValueKind.ValueType ||
            _typeDefinitions.ResolveTypeIdentity(aggregateType).LayoutKind ==
                CliTypeLayoutKind.Explicit)
        {
            return null;
        }

        return instruction.Operation switch
        {
            CilOperation.LoadField => ResolveField(
                aggregateType,
                instruction,
                instruction.Operand),
            CilOperation.Call => ResolveGetter(aggregateType, instruction),
            _ => null,
        };
    }

    private FieldProjection? ResolveGetter(
        CliTypeIdentity aggregateType,
        CilInstruction instruction)
    {
        var method = _calledMethods.Resolve(instruction);
        if (method is null ||
            method.Definition.IsStatic ||
            method.Definition.IsVirtual ||
            method.Definition.IsAbstract ||
            !method.Definition.HasManagedBody ||
            !method.DeclaringType.Equals(aggregateType) ||
            !method.Signature.ParameterSignatureTypes.IsEmpty ||
            !IsSupportedFieldType(method.Signature.ReturnSignatureType))
        {
            return null;
        }

        if (!_methodBodies.TryReadMethodBody(method, out var body) || !body.ExceptionRegions.IsEmpty)
        {
            return null;
        }

        var operations = body.Instructions
            .Where(candidate => candidate.Operation != CilOperation.Nop)
            .ToArray();
        if (operations.Length != 3 ||
            operations[0].Operation != CilOperation.LoadArgument ||
            operations[0].Operand is not CilOperand.Index { Value: 0 } ||
            operations[1].Operation != CilOperation.LoadField ||
            operations[2].Operation != CilOperation.Return ||
            ResolveFieldInstance(aggregateType, operations[1].Operand) is not { } field ||
            !method.Signature.ReturnSignatureType.Equals(field.FieldType))
        {
            return null;
        }

        return ResolveField(aggregateType, instruction, field);
    }

    private FieldProjection? ResolveField(
        CliTypeIdentity aggregateType,
        CilInstruction instruction,
        CilOperand operand) => ResolveField(
            aggregateType,
            instruction,
            ResolveFieldInstance(aggregateType, operand));

    private static FieldProjection? ResolveField(
        CliTypeIdentity aggregateType,
        CilInstruction instruction,
        FieldInstanceModel? field)
    {
        if (field is null ||
            field.Definition.IsStatic ||
            field.Definition.ExplicitOffset is not null ||
            !field.DeclaringType.Equals(aggregateType) ||
            !IsSupportedFieldType(field.FieldType))
        {
            return null;
        }

        return new FieldProjection(instruction, field);
    }

    private FieldInstanceModel? ResolveFieldInstance(
        CliTypeIdentity aggregateType,
        CilOperand operand) => operand switch
        {
            CilOperand.FieldInstance field => field.Value,
            CilOperand.Entity entity
                when aggregateType.Shape != CliTypeShape.GenericInstantiation =>
                ResolveDefinitionField(aggregateType, entity.Key),
            _ => null,
        };

    private FieldInstanceModel? ResolveDefinitionField(
        CliTypeIdentity aggregateType,
        EntityKey fieldKey)
    {
        var definition = _fields.GetField(fieldKey);
        var declaringType = _typeDefinitions.ResolveTypeIdentity(aggregateType);
        return definition.DeclaringType == declaringType.Key
            ? new FieldInstanceModel(
                definition,
                aggregateType,
                definition.SignatureType)
            : null;
    }

    private static bool IsSupportedFieldType(CliTypeIdentity type) =>
        type.StackKind is
            CliValueKind.I4 or
            CliValueKind.I8 or
            CliValueKind.F4 or
            CliValueKind.F8 or
            CliValueKind.NativeInt or
            CliValueKind.ManagedReference;
}
