using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IFieldSignatureContextResolver
{
    CliGenericContext Resolve(CliTypeIdentity declaringType);
}
