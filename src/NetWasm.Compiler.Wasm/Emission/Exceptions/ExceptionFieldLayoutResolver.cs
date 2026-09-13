using System;
using System.Linq;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Exceptions;

internal sealed class ExceptionFieldLayoutResolver(
    ITypeDescriptorSource descriptors,
    ITypeRepository types,
    IFieldRepository fields,
    IInstanceFieldLayoutProvider layouts) : IExceptionFieldLayoutResolver
{
    public int? Resolve(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        var exception = descriptors.TypeDescriptors
            .Select(descriptor => types.GetTypeDefinition(descriptor.Type))
            .SingleOrDefault(type => type.FullName == "System.Exception");
        if (exception is null)
        {
            return null;
        }

        var field = exception.Fields
            .Select(fields.GetField)
            .Single(candidate => candidate.Name == fieldName);
        return layouts.GetFieldLayout(field.Key).Offset;
    }
}
