using System;
using System.Linq;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis.Attributes;

internal sealed class AttributeUsageReader(
    ICustomAttributeDescriptorReader descriptors,
    ICustomAttributeValueDecoder values,
    CliTypeIdentity usageType) : IAttributeUsageReader
{
    public AttributeUsage Read(CliTypeIdentity attributeType)
    {
        var declared = descriptors.Read(attributeType).Where(attribute => attribute.AttributeType.Equals(usageType)).ToArray();
        if (declared.Length == 0) return new(Inherited: true, AllowMultiple: false);
        if (declared.Length != 1) throw Invalid("duplicate usage declarations");
        CustomAttributeValue<CliTypeIdentity> value;
        try
        {
            value = values.Decode(declared[0]);
        }
        catch (BadImageFormatException)
        {
            throw Invalid("malformed usage payload");
        }
        if (value.FixedArguments.Length != 1 || value.FixedArguments[0].Value is not int)
            throw Invalid("invalid usage constructor arguments");
        var inherited = true;
        var multiple = false;
        foreach (var argument in value.NamedArguments)
        {
            if (argument.Kind != CustomAttributeNamedArgumentKind.Property || argument.Value is not bool flag ||
                argument.Type.CanonicalName != "primitive:bool") throw Invalid("invalid usage named argument");
            switch (argument.Name)
            {
                case "Inherited": inherited = flag; break;
                case "AllowMultiple": multiple = flag; break;
                default: throw Invalid("unknown usage property");
            }
        }
        return new(inherited, multiple);
    }

    private static CompilerException Invalid(string reason) => new(new CompilerDiagnostic(
        DiagnosticCode.UnsupportedMetadata, $"custom attribute has {reason}"));
}
