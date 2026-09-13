using System;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal sealed class MetadataMethodBodyBlockReader(
    ISymbolFormatter symbols) : IMetadataMethodBodyBlockReader
{
    public MethodBodyBlock Read(PEReader source, MethodDefinitionModel method)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(method);
        if (!method.HasBody)
        {
            throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.InvalidCil,
                    "method has no CIL body",
                    symbols.Format(method)));
        }
        try
        {
            return source.GetMethodBody(method.RelativeVirtualAddress);
        }
        catch (BadImageFormatException exception)
        {
            throw new CompilerException(
                new CompilerDiagnostic(
                    DiagnosticCode.InvalidCil,
                    $"method body is malformed: {exception.Message}",
                    symbols.Format(method)));
        }
    }
}
