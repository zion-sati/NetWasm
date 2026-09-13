using System;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class StaticFieldValueLayoutResolver(
    IFieldRepository fields,
    ReachableProgram program,
    IValueLayoutResolver valueLayouts) : IStaticFieldValueLayoutResolver
{
    private readonly IFieldRepository _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly IValueLayoutResolver _valueLayouts = valueLayouts ??
        throw new ArgumentNullException(nameof(valueLayouts));

    public void Resolve()
    {
        foreach (var fieldKey in _program.Fields)
        {
            var field = _fields.GetField(fieldKey);
            if (field.IsStatic && !field.SignatureType.ContainsGenericParameters)
                _valueLayouts.Resolve(field.SignatureType);
        }
        foreach (var field in _program.ConstructedFields.Values.Where(field =>
                     field.Definition.IsStatic))
        {
            _valueLayouts.Resolve(field.FieldType);
        }
    }
}
