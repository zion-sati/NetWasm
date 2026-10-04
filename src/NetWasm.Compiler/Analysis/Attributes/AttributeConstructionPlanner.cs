using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.IntermediateRepresentation.Attributes;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeConstructionPlanner(
    IMethodInstanceResolver methods,
    ICustomAttributeValueDecoder values,
    ICustomAttributeMemberResolver members,
    IAttributeArgumentDecoder arguments,
    IBaseTypeResolver bases) : IAttributeConstructionPlanner
{
    public AttributeConstructionPlan Plan(CustomAttributeDescriptor attribute)
    {
        ArgumentNullException.ThrowIfNull(attribute);
        var constructor = methods.ResolveMethodInstance(attribute.Source, attribute.ConstructorToken,
            "custom attribute constructor", 0, attribute.GenericContext);
        System.Reflection.Metadata.CustomAttributeValue<CliTypeIdentity> value;
        try
        {
            value = values.Decode(attribute);
        }
        catch (BadImageFormatException)
        {
            throw Unsupported("custom attribute has an invalid payload");
        }
        if (value.FixedArguments.Length != constructor.Signature.ParameterSignatureTypes.Length)
            throw Unsupported("custom attribute constructor payload has the wrong arity");
        return new(attribute.AttributeType, constructor,
            [.. value.FixedArguments.Select((argument, index) =>
                arguments.Decode(argument, constructor.Signature.ParameterSignatureTypes[index]))],
            [.. value.NamedArguments.Select(argument =>
            {
                var name = argument.Name ?? throw Unsupported("custom attribute has an unnamed named argument");
                for (var current = attribute.AttributeType; current is not null; current = bases.Resolve(current))
                {
                    if (members.Resolve(current, name, argument.Kind) is not { } member) continue;
                    var type = member.Field?.FieldType ?? member.Setter!.Signature.ParameterSignatureTypes[^1];
                    return new AttributeNamedArgumentPlan(name, arguments.Decode(new(argument.Type, argument.Value), type))
                    {
                        Field = member.Field,
                        Setter = member.Setter,
                    };
                }
                throw Unsupported($"custom attribute named argument '{name}' has no writable member");
            })]);
    }

    private static CompilerException Unsupported(string message) =>
        new(new CompilerDiagnostic(DiagnosticCode.UnsupportedMetadata, message));
}
