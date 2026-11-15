// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Security.Cryptography;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class MalformedInputGeneratorTests
{
    private static readonly string[] ExpectedBaseInvariants =
    [
        "unknown-opcode",
        "truncated-operand",
        "invalid-branch-target",
        "invalid-argument-index",
        "invalid-local-index",
        "evaluation-stack-underflow",
        "invalid-return-stack",
        "evaluation-stack-type-mismatch",
        "evaluation-stack-merge-mismatch",
        "invalid-metadata-token",
        "malformed-prefix",
        "invalid-metadata-root",
    ];

    private static readonly string[] ExpectedExceptionInvariants =
    [
        "illegal-branch-into-filter-region",
        "illegal-branch-into-handler-region",
        "illegal-branch-out-of-protected-region",
        "endfilter-outside-filter-region",
        "endfinally-outside-finally-region",
        "illegal-return-from-finally-region",
        "illegal-leave-from-finally-region",
        "filter-region-missing-endfilter",
        "finally-region-missing-endfinally",
    ];

    [Fact]
    public void MaterializesExactByteIdenticalMutationsFromAValidAssembly()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileBaseline(assets);
        var generator = new MalformedInputGenerator();
        var first = generator.Generate(
            assembly,
            Path.Combine(assets.Directory, "first"));
        var second = generator.Generate(
            assembly,
            Path.Combine(assets.Directory, "second"));

        Assert.Equal(ExpectedBaseInvariants, first.Select(item => item.Invariant));
        Assert.Equal(first.Select(item => item.Name), second.Select(item => item.Name));
        for (var index = 0; index < first.Length; index++)
        {
            Assert.Equal(first[index].Sha256, second[index].Sha256);
            Assert.Equal(first[index].Invariant, second[index].Invariant);
            Assert.False(string.IsNullOrWhiteSpace(first[index].Invariant));
            Assert.Equal(
                File.ReadAllBytes(first[index].Path),
                File.ReadAllBytes(second[index].Path));
            Assert.Equal(
                first[index].Sha256,
                Convert.ToHexStringLower(SHA256.HashData(
                    File.ReadAllBytes(first[index].Path))));
            Assert.NotEqual(first[index].OriginalHex, first[index].ReplacementHex);
        }
    }

    [Fact]
    public void MaterializesOneDeterministicViolationForEachExceptionRegionRule()
    {
        using var assets = TestAssets.Create();
        var assembly = CompileExceptionBaseline(assets);
        var generator = new MalformedInputGenerator();
        var first = generator.GenerateExceptionHandling(
            assembly,
            Path.Combine(assets.Directory, "eh-first"));
        var second = generator.GenerateExceptionHandling(
            assembly,
            Path.Combine(assets.Directory, "eh-second"));

        Assert.Equal(ExpectedExceptionInvariants, first.Select(item => item.Invariant));
        Assert.Equal(first.Select(item => item.Name), second.Select(item => item.Name));
        Assert.Equal(first.Select(item => item.Invariant).Distinct().Count(), first.Length);
        for (var index = 0; index < first.Length; index++)
        {
            Assert.Equal(first[index].Sha256, second[index].Sha256);
            Assert.Equal(
                File.ReadAllBytes(first[index].Path),
                File.ReadAllBytes(second[index].Path));
        }
    }

    internal static string CompileBaseline(TestAssets assets) => assets.CompileSource(
        "MalformedFuzzBaseline",
        """
        namespace MalformedFuzzBaseline;
        public static class EntryPoint
        {
            public static int Run(int input)
            {
                if (input > 0) return input + 1;
                return input - 1;
            }
        }
        """);

    internal static string CompileExceptionBaseline(TestAssets assets) =>
        assets.CompileSource(
            "MalformedEhBaseline",
            """
            namespace MalformedEhBaseline;
            public static class EntryPoint
            {
                private static bool Accept(int value) => value > 0;

                public static int Run(int input)
                {
                    try
                    {
                        if (input < 0) throw new System.ArgumentException();
                        return input + 1;
                    }
                    catch (System.ArgumentException) when (Accept(input))
                    {
                        return input + 2;
                    }
                    finally
                    {
                        input += 3;
                    }
                }
            }
            """);
}
