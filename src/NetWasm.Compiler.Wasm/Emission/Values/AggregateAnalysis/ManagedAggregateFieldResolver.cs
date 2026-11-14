using System;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Values.AggregateAnalysis;

internal sealed class ManagedAggregateFieldResolver(
    IFieldRepository fieldRepository,
    ITypeDefinitionResolver typeDefinitions,
    IInstanceFieldLayoutProvider fields) : IManagedAggregateFieldResolver
{
    private readonly IFieldRepository _fieldRepository = fieldRepository ??
        throw new ArgumentNullException(nameof(fieldRepository));
    private readonly ITypeDefinitionResolver _typeDefinitions = typeDefinitions ??
        throw new ArgumentNullException(nameof(typeDefinitions));
    private readonly IInstanceFieldLayoutProvider _fields = fields ??
        throw new ArgumentNullException(nameof(fields));

    public bool TryResolve(
        CliTypeIdentity aggregateType,
        CilOperand operand,
        out FieldLayout layout,
        out CliTypeIdentity fieldType)
    {
        ArgumentNullException.ThrowIfNull(aggregateType);
        ArgumentNullException.ThrowIfNull(operand);
        switch (operand)
        {
            case CilOperand.FieldInstance instance
                when !instance.Value.Definition.IsStatic &&
                    instance.Value.DeclaringType.Equals(aggregateType):
                layout = _fields.GetFieldLayout(instance.Value);
                fieldType = instance.Value.FieldType;
                return true;
            case CilOperand.Entity entity
                when aggregateType.Shape != CliTypeShape.GenericInstantiation:
                var definition = _fieldRepository.GetField(entity.Key);
                if (!definition.IsStatic &&
                    definition.DeclaringType ==
                        _typeDefinitions.ResolveTypeIdentity(aggregateType).Key)
                {
                    layout = _fields.GetFieldLayout(entity.Key);
                    fieldType = definition.SignatureType;
                    return true;
                }
                break;
        }
        layout = default;
        fieldType = null!;
        return false;
    }
}
