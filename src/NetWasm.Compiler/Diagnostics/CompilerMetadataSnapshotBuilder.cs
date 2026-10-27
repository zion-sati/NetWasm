using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Diagnostics;

internal sealed class CompilerMetadataSnapshotBuilder(
    ICompilerCilOperandFormatter operandFormatter) : ICompilerMetadataSnapshotBuilder
{
    private readonly ICompilerCilOperandFormatter _operandFormatter =
        operandFormatter ?? throw new ArgumentNullException(nameof(operandFormatter));

    public CompilerMetadataArtifact BuildMetadata(
        MetadataCompilationSnapshot metadata,
        IMethodBodyReader bodies,
        ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(symbols);
        return new CompilerMetadataArtifact(
            metadata.EntryAssemblyIdentity.ToString(),
            [.. metadata.Types
                .OrderBy(type => type.Key.Assembly.Name, StringComparer.Ordinal)
                .ThenBy(type => type.Key.MetadataToken)
                .Select(type => new CompilerMetadataTypeArtifact(
                    type.Key.ToString(),
                    type.FullName,
                    type.IsValueType,
                    type.GenericArity))],
            [.. metadata.EntryAssemblyMethods
                .OrderBy(method => method.Key.MetadataToken)
                .Select(method => DecodeMethod(bodies, symbols, method))]);
    }

    private CompilerMetadataMethodArtifact DecodeMethod(
        IMethodBodyReader bodies,
        ISymbolFormatter symbols,
        MethodDefinitionModel method)
    {
        if (!method.HasBody)
        {
            return Method(symbols, method, hasBody: false, decodeError: null, [], []);
        }
        try
        {
            var body = bodies.ReadMethodBody(method);
            return Method(
                symbols,
                method,
                hasBody: true,
                decodeError: null,
                [.. body.Instructions.Select(BuildInstruction)],
                [.. body.ExceptionRegions]);
        }
        catch (Exception exception)
        {
            return Method(symbols, method, hasBody: true, exception.ToString(), [], []);
        }
    }

    private static CompilerMetadataMethodArtifact Method(
        ISymbolFormatter symbols,
        MethodDefinitionModel method,
        bool hasBody,
        string? decodeError,
        CompilerCilInstructionArtifact[] instructions,
        CilExceptionRegion[] exceptionRegions) => new(
        symbols.Format(method),
        method.Key.MetadataToken,
        method.GenericArity,
        hasBody,
        decodeError,
        instructions,
        exceptionRegions);

    private CompilerCilInstructionArtifact BuildInstruction(CilInstruction instruction) => new(
        instruction.Offset,
        instruction.NextOffset,
        instruction.Operation.ToString(),
        instruction.Operand is CilOperand.None
            ? null
            : _operandFormatter.FormatOperand(instruction.Operand));
}
