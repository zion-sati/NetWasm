using System;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataEntityHandleReader : IMetadataEntityHandleReader
{
    public EntityHandle Read(
        int token,
        string kind,
        string method,
        int ilOffset)
    {
        try
        {
            return MetadataTokens.EntityHandle(token);
        }
        catch (ArgumentException)
        {
            throw new CompilerException(new CompilerDiagnostic(
                DiagnosticCode.InvalidCil,
                $"invalid {kind} token 0x{token:x8}",
                method,
                ilOffset));
        }
    }
}
