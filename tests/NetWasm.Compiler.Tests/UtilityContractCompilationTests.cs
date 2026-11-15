// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class UtilityContractCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void CompilerPreservesLazyTextAndFormattableProviderIdentity(
        bool optimize,
        WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            using System.Globalization;

            namespace UtilityContractFixture;

            public sealed class ProviderProbe : FormattableString
            {
                public override string Format => "probe";
                public override object?[] GetArguments() => Array.Empty<object?>();
                public override int ArgumentCount => 0;
                public override object? GetArgument(int index) => throw new IndexOutOfRangeException();
                public override string ToString(IFormatProvider? provider) =>
                    object.ReferenceEquals(provider, CultureInfo.InvariantCulture)
                        ? "culture"
                        : provider is null ? "null" : "other";
            }

            public sealed class NullText
            {
                public override string ToString() => null!;
            }

            public sealed class CreatedText
            {
                public override string ToString() => "created";
            }

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var pending = new Lazy<CreatedText>(() => new CreatedText());
                    if (pending.ToString() != "Value is not created." || pending.IsValueCreated)
                    {
                        return 1;
                    }
                    _ = pending.Value;
                    if (pending.ToString() != "created") return 2;

                    var nullValue = new Lazy<NullText?>(() => null);
                    _ = nullValue.Value;
                    try
                    {
                        _ = nullValue.ToString();
                        return 3;
                    }
                    catch (NullReferenceException)
                    {
                    }

                    var nullText = new Lazy<NullText>(() => new NullText());
                    _ = nullText.Value;
                    if (nullText.ToString() is not null) return 4;

                    var probe = new ProviderProbe();
                    if (FormattableString.Invariant(probe) != "culture" ||
                        FormattableString.CurrentCulture(probe) != "culture" ||
                        probe.ToString() != "culture")
                    {
                        return 5;
                    }

                    return input;
                }
            }
            """;

        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        Assert.Equal(42, executor.Execute(new CompilationScenario(
            $"UtilityContractFixture_{optimize}_{target}",
            source,
            "UtilityContractFixture.EntryPoint",
            optimize,
            target,
            42,
            [])));
    }
}
