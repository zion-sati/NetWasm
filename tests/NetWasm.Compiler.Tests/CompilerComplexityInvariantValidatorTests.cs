// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Validation;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerComplexityInvariantValidatorTests
{
    [Fact]
    public void AcceptsExactlyOneEmissionForEveryReachableOriginalBlock()
    {
        var validator = CreateValidator();
        validator.Validate(Report(Metric()));
    }

    [Fact]
    public void RejectsAPathKeyedBlockCloneOrMissingOriginalBody()
    {
        var validator = CreateValidator();

        var duplicate = Assert.Throws<CompilerException>(() => validator.Validate(
            Report(Metric() with
            {
                OriginalBodyEmissionCount = 4,
                DuplicatedOriginalBlocks = [1],
            })));
        Assert.Contains("emitted more than once", duplicate.Message);

        var missing = Assert.Throws<CompilerException>(() => validator.Validate(
            Report(Metric() with
            {
                OriginalBodyEmissionCount = 2,
                MissingOriginalBlocks = [2],
            })));
        Assert.Contains("were not emitted", missing.Message);

        var countMismatch = Assert.Throws<CompilerException>(() => validator.Validate(
            Report(Metric() with { OriginalBodyEmissionCount = 2 })));
        Assert.Contains("for 3 reachable blocks", countMismatch.Message);

        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
    }

    [Fact]
    public void LogsEveryViolationWhenWarningsAreEnabled()
    {
        var logger = new CompilerTestLogger<CompilerComplexityInvariantValidator>();
        var validator = new CompilerComplexityInvariantValidator(
            new CompilerInvariantExceptionFactory(),
            logger);
        var metric = Metric() with
        {
            OriginalBodyEmissionCount = 1,
            MissingOriginalBlocks = [1],
            DuplicatedOriginalBlocks = [2],
        };

        Assert.Throws<CompilerException>(() => validator.Validate(Report(metric)));
        Assert.Equal(3, logger.EntryCount);
        Assert.Equal(LogLevel.Warning, logger.Level);
    }

    private static CompilerComplexityReport Report(
        CompilerMethodComplexityMetric metric) => new(
        [metric],
        metric.CilInstructionCount,
        metric.OriginalBlockCount,
        metric.CfgEdgeCount,
        metric.WasmInstructionCount,
        metric.WasmBodyBytes);

    private static ICompilerComplexityInvariantValidator CreateValidator() =>
        Assert.IsAssignableFrom<ICompilerComplexityInvariantValidator>(
            new CompilerComplexityInvariantValidator(
                new CompilerInvariantExceptionFactory(),
                NullLogger<CompilerComplexityInvariantValidator>.Instance));

    private static CompilerMethodComplexityMetric Metric() => new(
        "Example::Run",
        9,
        3,
        3,
        4,
        0,
        1,
        4,
        1,
        3,
        ImmutableArray<int>.Empty,
        ImmutableArray<int>.Empty,
        11,
        29,
        7,
        65_536);
}
