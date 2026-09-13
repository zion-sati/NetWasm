using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Runtime;

internal sealed class ValueTypeEqualityFieldPlanner(
    ITypeDefinitionResolver types,
    IFieldRepository fields,
    IInstanceFieldLayoutProvider layouts,
    IValueLayoutProvider values) : IValueTypeEqualityFieldPlanner
{
    public ImmutableArray<ValueTypeEqualityField> Plan(CliTypeIdentity type)
    {
        var result = ImmutableArray.CreateBuilder<ValueTypeEqualityField>();
        Add(type, 0, [], result);
        return result.ToImmutable();
    }

    private void Add(
        CliTypeIdentity type,
        int baseOffset,
        ImmutableHashSet<string> active,
        ImmutableArray<ValueTypeEqualityField>.Builder result)
    {
        if (!type.IsValueType || type.Shape == CliTypeShape.Primitive ||
            type.Shape is CliTypeShape.ManagedByReference or
                CliTypeShape.UnmanagedPointer)
        {
            result.Add(new(baseOffset, type));
            return;
        }

        if (active.Contains(type.CanonicalName))
        {
            throw new InvalidOperationException(
                $"recursive value equality layout for '{type.CanonicalName}'");
        }
        var next = active.Add(type.CanonicalName);
        var definitionIdentity = type.Shape == CliTypeShape.GenericInstantiation
            ? type.ElementType!
            : type;
        var definition = types.ResolveTypeIdentity(definitionIdentity);
        var typeArguments = type.Shape == CliTypeShape.GenericInstantiation
            ? type.TypeArguments
            : [];
        foreach (var field in definition.Fields
                     .Select(fields.GetField)
                     .Where(field => !field.IsStatic)
                     .OrderBy(field => field.Key.MetadataToken))
        {
            var fieldType = field.SignatureType.Substitute(typeArguments);
            var instance = new FieldInstanceModel(field, type, fieldType);
            var layout = layouts.GetFieldLayout(instance);
            var repetitions = definition.InlineArrayLength == 0
                ? 1
                : definition.InlineArrayLength;
            var fieldSize = values.GetValueLayout(fieldType).Size;
            for (var index = 0; index < repetitions; index++)
            {
                Add(
                    fieldType,
                    checked(baseOffset + layout.Offset + index * fieldSize),
                    next,
                    result);
            }
        }
    }
}
