using System;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler;
using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Validation;

internal sealed class CompilerComplexityInvariantValidator(
    ICompilerInvariantExceptionFactory exceptions,
    ILogger<CompilerComplexityInvariantValidator> logger)
    :
    ICompilerComplexityInvariantValidator
{
    private static readonly Action<ILogger, string, int, int, Exception?> LogDuplicatedOriginalBlocks =
        LoggerMessage.Define<string, int, int>(
            LogLevel.Warning,
            new EventId(4200, nameof(LogDuplicatedOriginalBlocks)),
            "Original block emission conflict {EmissionIdentity}: count {Count}, first block {FirstBlock}.");

    private static readonly Action<ILogger, string, int, int, Exception?> LogMissingOriginalBlocks =
        LoggerMessage.Define<string, int, int>(
            LogLevel.Warning,
            new EventId(4201, nameof(LogMissingOriginalBlocks)),
            "Missing original block emission {EmissionIdentity}: count {Count}, first block {FirstBlock}.");

    private static readonly Action<ILogger, string, int, int, Exception?> LogOriginalBlockCountMismatch =
        LoggerMessage.Define<string, int, int>(
            LogLevel.Warning,
            new EventId(4202, nameof(LogOriginalBlockCountMismatch)),
            "Original block emission count mismatch {EmissionIdentity}: emitted {EmittedCount}, expected {ExpectedCount}.");

    public void Validate(CompilerComplexityReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (logger.IsEnabled(LogLevel.Warning))
        {
            foreach (var method in report.Methods)
            {
                if (!method.DuplicatedOriginalBlocks.IsDefaultOrEmpty)
                {
                    LogDuplicatedOriginalBlocks(
                        logger,
                        method.Identity,
                        method.DuplicatedOriginalBlocks.Length,
                        method.DuplicatedOriginalBlocks[0],
                        null);
                }

                if (!method.MissingOriginalBlocks.IsDefaultOrEmpty)
                {
                    LogMissingOriginalBlocks(
                        logger,
                        method.Identity,
                        method.MissingOriginalBlocks.Length,
                        method.MissingOriginalBlocks[0],
                        null);
                }

                if (method.OriginalBodyEmissionCount !=
                    method.ReachableOriginalBlockCount)
                {
                    LogOriginalBlockCountMismatch(
                        logger,
                        method.Identity,
                        method.OriginalBodyEmissionCount,
                        method.ReachableOriginalBlockCount,
                        null);
                }
            }
        }
        foreach (var method in report.Methods)
        {
            if (!method.MissingOriginalBlocks.IsEmpty)
            {
                throw exceptions.Create(
                    "reachable original blocks were not emitted: " +
                    string.Join(',', method.MissingOriginalBlocks),
                    method.Identity);
            }
            if (!method.DuplicatedOriginalBlocks.IsEmpty)
            {
                throw exceptions.Create(
                    "original block bodies were emitted more than once: " +
                    string.Join(',', method.DuplicatedOriginalBlocks),
                    method.Identity);
            }
            if (method.OriginalBodyEmissionCount !=
                method.ReachableOriginalBlockCount)
            {
                throw exceptions.Create(
                    $"emitted {method.OriginalBodyEmissionCount} original block bodies " +
                    $"for {method.ReachableOriginalBlockCount} reachable blocks",
                    method.Identity);
            }
        }
    }
}
