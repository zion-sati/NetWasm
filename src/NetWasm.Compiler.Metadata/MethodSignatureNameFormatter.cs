using System;
using System.Reflection.Metadata;

namespace NetWasm.Compiler.Metadata;

// Adapts a method definition to the signature text a substitution matches it by: its return type, name and parameter
// types ("System.Boolean IsEnabled(System.Diagnostics.Tracing.EventLevel,System.Diagnostics.Tracing.EventKeywords)"),
// each type named by the given Strategy.
internal sealed class MethodSignatureNameFormatter(ISignatureTypeProvider<string, object?> types) : IMethodSignatureFormatter
{
    private readonly ISignatureTypeProvider<string, object?> _types = types ?? throw new ArgumentNullException(nameof(types));

    public string Format(MetadataReader reader, MethodDefinition method)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var signature = method.DecodeSignature(_types, null);
        return signature.ReturnType + " " + reader.GetString(method.Name) + "(" + string.Join(",", signature.ParameterTypes) + ")";
    }
}
