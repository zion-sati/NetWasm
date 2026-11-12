using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataCallSiteSignatureResolver : IMetadataCallSiteSignatureResolver
{
    private const string CallConvCdecl = "System.Runtime.CompilerServices.CallConvCdecl";

    public CilOperand.CallSite Resolve(
        MetadataAssemblySnapshot source,
        int metadataToken,
        CliGenericContext genericContext,
        string method,
        int offset)
    {
        var handle = MetadataTokens.EntityHandle(metadataToken);
        if (handle.Kind != HandleKind.StandaloneSignature)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.UnsupportedMetadata,
                $"calli token 0x{metadataToken:x8} is not a standalone signature",
                method,
                offset));
        }

        var signature = source.Reader.GetStandaloneSignature(
            (StandaloneSignatureHandle)handle).DecodeMethodSignature(
                new SignatureTypeProvider(
                    source.Identity,
                    source.Reader,
                    source.AssemblyIdentityAliases),
                genericContext);
        // An unmanaged pointer uses the platform's C ABI, as a DllImport does: its own convention
        // (unmanaged, unmanaged[Cdecl]) or Cdecl named once as a modifier of the return type.
        var convention = signature.Header.CallingConvention;
        var returnType = signature.ReturnType;
        var modifiers = 0;
        while (returnType.Shape == CliTypeShape.Modified)
        {
            if (convention != SignatureCallingConvention.Unmanaged ||
                returnType.CustomModifier!.FullName != CallConvCdecl || ++modifiers != 1)
            {
                throw Unsupported(returnType.CustomModifier!.FullName ?? "a modifier");
            }
            returnType = returnType.ElementType!;
        }
        if (convention is not (SignatureCallingConvention.Default or
            SignatureCallingConvention.CDecl or SignatureCallingConvention.Unmanaged))
        {
            throw Unsupported(convention.ToString());
        }

        return new(
            new MethodSignatureModel(returnType, signature.ParameterTypes),
            convention != SignatureCallingConvention.Default);

        CompilerException Unsupported(string name) => new(new CompilerDiagnostic(
            DiagnosticCode.NativeInterop,
            $"calli calling convention '{name}' is not supported; unmanaged function pointers use the default C ABI or Cdecl",
            method,
            offset));
    }
}
