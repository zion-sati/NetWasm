using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataCallSiteSignatureResolver : IMetadataCallSiteSignatureResolver
{
    public MethodSignatureModel Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        CliGenericContext genericContext)
    {
        var handle = MetadataTokens.EntityHandle(metadataToken);
        if (handle.Kind != HandleKind.StandaloneSignature)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"calli token 0x{metadataToken:x8} is not a standalone signature"));
        }

        var signature = source.Reader.GetStandaloneSignature(
            (StandaloneSignatureHandle)handle).DecodeMethodSignature(
                new SignatureTypeProvider(
                    source.Identity,
                    source.Reader,
                    source.AssemblyIdentityAliases),
                genericContext);
        if (signature.Header.CallingConvention != SignatureCallingConvention.Default)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"calli calling convention '{signature.Header.CallingConvention}' is not supported"));
        }

        return new MethodSignatureModel(signature.ReturnType, signature.ParameterTypes);
    }
}
