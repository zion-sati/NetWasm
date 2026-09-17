using System;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ControlFlow.Structured;

internal sealed record StructuredMethodConstruction(
    StructuredMethodHeader Header,
    StructuredBlockId EntryBlock,
    ImmutableDictionary<StructuredBlockId, StructuredBlockDefinition> Blocks,
    StructuredSequence Body,
    ImmutableArray<StructuredExceptionGroupId> TopLevelExceptionGroups,
    ImmutableDictionary<StructuredExceptionGroupId, StructuredExceptionGroup> ExceptionGroups,
    ImmutableDictionary<int, ImmutableArray<CliValueKind>> InstructionEntryStacks);

internal interface IStructuredMethodFactory
{
    StructuredMethod Create(StructuredMethodConstruction construction);
}

internal sealed class StructuredMethodValidationException(
    InvalidOperationException innerException) :
    Exception("The structured method failed validation.", innerException);

internal sealed class StructuredMethodFactory(
    IStructuredMethodValidator validator) : IStructuredMethodFactory
{
    private readonly IStructuredMethodValidator _validator = validator ??
        throw new ArgumentNullException(nameof(validator));

    public StructuredMethod Create(StructuredMethodConstruction construction)
    {
        ArgumentNullException.ThrowIfNull(construction);
        var method = new StructuredMethod(
            construction.Header,
            construction.EntryBlock,
            construction.Blocks,
            construction.Body,
            construction.TopLevelExceptionGroups,
            construction.ExceptionGroups,
            construction.InstructionEntryStacks);
        try
        {
            _validator.Validate(method);
        }
        catch (InvalidOperationException exception)
        {
            throw new StructuredMethodValidationException(exception);
        }
        return method;
    }
}
