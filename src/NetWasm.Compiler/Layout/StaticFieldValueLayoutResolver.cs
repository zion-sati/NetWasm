using System;
using System.Linq;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Layout;

internal sealed class StaticFieldValueLayoutResolver(
    IFieldRepository fields,
    ReachableProgram program,
    IValueLayoutResolver valueLayouts,
    ITypeIdentityResolver identities,
    IFieldLayoutFailureBinder failures) : IStaticFieldValueLayoutResolver
{
    private readonly IFieldLayoutFailureBinder _failures = failures ?? throw new ArgumentNullException(nameof(failures));
    private readonly IFieldRepository _fields = fields ??
        throw new ArgumentNullException(nameof(fields));
    private readonly ReachableProgram _program = program ??
        throw new ArgumentNullException(nameof(program));
    private readonly IValueLayoutResolver _valueLayouts = valueLayouts ??
        throw new ArgumentNullException(nameof(valueLayouts));

    private readonly ITypeIdentityResolver _identities = identities ??
        throw new ArgumentNullException(nameof(identities));

    public void Resolve()
    {
        foreach (var fieldKey in _program.Fields)
        {
            var field = _fields.GetField(fieldKey);
            if (field.IsStatic && !field.SignatureType.ContainsGenericParameters)
                ResolveStorage(field, field.SignatureType, null);
        }
        foreach (var field in _program.ConstructedFields.Values.Where(field =>
                     field.Definition.IsStatic))
        {
            ResolveStorage(field.Definition, field.FieldType, field.DeclaringType);
        }
    }

    private void ResolveStorage(FieldDefinitionModel field, CliTypeIdentity type, CliTypeIdentity? owner)
    {
        try
        {
            _valueLayouts.Resolve(type);
        }
        catch (CompilerException failure)
        {
            throw _failures.Bind(failure, new(field.Key, type.CanonicalName)
            {
                DeclaringType = owner ?? _identities.GetTypeIdentity(field.DeclaringType),
            });
        }
    }

}
