using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class FieldSignatureContextResolver : IFieldSignatureContextResolver
{
    public CliGenericContext Resolve(CliTypeIdentity declaringType) =>
        declaringType.Shape == CliTypeShape.GenericInstantiation
            ? new CliGenericContext(declaringType.TypeArguments, [])
            : CliGenericContext.Empty;
}
