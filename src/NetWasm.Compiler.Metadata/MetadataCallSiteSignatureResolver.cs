using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataCallSiteSignatureResolver : IMetadataCallSiteSignatureResolver
{
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
        // A managed pointer keeps its signature, modifiers and all (a ref readonly return's modreq).
        var convention = signature.Header.CallingConvention;
        if (convention == SignatureCallingConvention.Default)
        {
            return new(new MethodSignatureModel(signature.ReturnType, signature.ParameterTypes), false);
        }

        // An unmanaged pointer is called with the C ABI, which reads no modifier, whichever convention
        // desktop .NET accepts it names (UnmanagedCallingConventions): its header's (unmanaged[Cdecl],
        // [Stdcall], [Thiscall]), or with the unmanaged header, the CallConv types the return type's
        // optional modifiers name.
        var returnType = signature.ReturnType;
        var conventions = new List<string?>();
        while (returnType.Shape == CliTypeShape.Modified)
        {
            if (!returnType.IsRequiredModifier)
            {
                conventions.Add(returnType.CustomModifier!.FullName);
            }
            returnType = returnType.ElementType!;
        }
        var unsupported = convention switch
        {
            SignatureCallingConvention.CDecl or SignatureCallingConvention.StdCall or
                SignatureCallingConvention.ThisCall => null,
            SignatureCallingConvention.Unmanaged => UnmanagedCallingConventions.Unsupported(conventions),
            _ => $"calling convention '{convention}' is not supported",
        };
        if (unsupported is not null)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.NativeInterop,
                $"calli {unsupported}; an unmanaged function pointer is called with the C ABI: unmanaged, " +
                "unmanaged[Cdecl], [Stdcall] or [Thiscall], with SuppressGCTransition or MemberFunction",
                method,
                offset));
        }

        return new(new MethodSignatureModel(returnType, signature.ParameterTypes), true);
    }
}
