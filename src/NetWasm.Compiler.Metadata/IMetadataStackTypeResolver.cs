using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IMetadataStackTypeResolver
{
    CliTypeIdentity Resolve(CliTypeIdentity type);

    CliTypeIdentity Resolve(CliTypeIdentity type, CliGenericContext genericContext);
    MethodSignatureModel Resolve(MethodSignatureModel signature);

    MethodSignatureModel Resolve(MethodSignatureModel signature, CliGenericContext genericContext);
}
