using System;
using System.Linq;

namespace NetWasm.Hosting.Execution;

/// <summary>Validates execution outcome semantics independently of transport and framework interpretation.</summary>
public sealed class NetWasmExecutionResultValidator : INetWasmExecutionResultValidator
{
    public void Validate(NetWasmExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.SchemaVersion != 1)
        {
            throw new ArgumentException("Unsupported NetWasm execution result schema.", nameof(result));
        }

        if (!Enum.IsDefined(result.CompletionKind))
        {
            throw new ArgumentException("Unsupported NetWasm completion kind.", nameof(result));
        }

        if (result.CleanupFailures.IsDefault)
        {
            throw new ArgumentException("Cleanup failures must be an explicit ordered collection.", nameof(result));
        }

        foreach (var failure in result.CleanupFailures)
        {
            ValidateFailure(failure);
            if (failure.Phase is not (NetWasmFailurePhase.Cleanup or NetWasmFailurePhase.Output))
            {
                throw new ArgumentException("Cleanup failure records require cleanup or output phase.", nameof(result));
            }
        }

        if (result.CompletionKind == NetWasmCompletionKind.Normal)
        {
            if (result.ExitCode is null || result.PrimaryFailure is not null || !result.CleanupFailures.IsEmpty)
            {
                throw new ArgumentException("Normal completion requires only a signed exit code.", nameof(result));
            }

            return;
        }

        if (result.ExitCode is not null || result.PrimaryFailure is null)
        {
            throw new ArgumentException("Non-normal completion requires one primary failure and no exit code.", nameof(result));
        }

        ValidateFailure(result.PrimaryFailure);
    }

    private static void ValidateFailure(NetWasmExecutionFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (!Enum.IsDefined(failure.Phase))
        {
            throw new ArgumentException("Unsupported NetWasm failure phase.", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failure.Code);
        if (!char.IsAsciiLetterLower(failure.Code[0])
            || !failure.Code.All(character => char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) || character is '-' or '.'))
        {
            throw new ArgumentException("Execution failure codes must use canonical lowercase syntax.", nameof(failure));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(failure.Message);
        if (failure.Message.AsSpan().Trim().Length != failure.Message.Length || failure.Message.Any(char.IsControl))
        {
            throw new ArgumentException("Execution failure messages must be canonical single-line text.", nameof(failure));
        }
    }
}
