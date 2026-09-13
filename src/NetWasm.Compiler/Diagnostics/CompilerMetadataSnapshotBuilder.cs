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

    public object BuildMetadata(
        MetadataCompilationSnapshot metadata,
        IMethodBodyReader bodies,
        ISymbolFormatter symbols)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(bodies);
        ArgumentNullException.ThrowIfNull(symbols);
        return new
        {
            EntryAssembly = metadata.EntryAssemblyIdentity.ToString(),
            Types = metadata.Types
                .OrderBy(type => type.Key.Assembly.Name, StringComparer.Ordinal)
                .ThenBy(type => type.Key.MetadataToken)
                .Select(type => new
                {
                    Identity = type.Key.ToString(),
                    type.FullName,
                    type.IsValueType,
                    type.GenericArity,
                })
                .ToArray(),
            Methods = metadata.EntryAssemblyMethods
                .OrderBy(method => method.Key.MetadataToken)
                .Select(method => DecodeMethod(bodies, symbols, method))
                .ToArray(),
        };
    }

    private object DecodeMethod(
        IMethodBodyReader bodies,
        ISymbolFormatter symbols,
        MethodDefinitionModel method)
    {
        if (!method.HasBody)
        {
            return new
            {
                Identity = symbols.Format(method),
                method.Key.MetadataToken,
                method.GenericArity,
                HasBody = false,
                DecodeError = (string?)null,
                Instructions = Array.Empty<object>(),
                ExceptionRegions = Array.Empty<CilExceptionRegion>(),
            };
        }
        try
        {
            var body = bodies.ReadMethodBody(method);
            return new
            {
                Identity = symbols.Format(method),
                method.Key.MetadataToken,
                method.GenericArity,
                HasBody = true,
                DecodeError = (string?)null,
                Instructions = body.Instructions.Select(BuildInstruction).ToArray(),
                ExceptionRegions = body.ExceptionRegions.ToArray(),
            };
        }
        catch (Exception exception)
        {
            return new
            {
                Identity = symbols.Format(method),
                method.Key.MetadataToken,
                method.GenericArity,
                HasBody = true,
                DecodeError = exception.ToString(),
                Instructions = Array.Empty<object>(),
                ExceptionRegions = Array.Empty<CilExceptionRegion>(),
            };
        }
    }

    private object BuildInstruction(CilInstruction instruction) => new
    {
        instruction.Offset,
        instruction.NextOffset,
        Operation = instruction.Operation.ToString(),
        Operand = instruction.Operand is CilOperand.None
            ? null
            : _operandFormatter.FormatOperand(instruction.Operand),
    };

}
