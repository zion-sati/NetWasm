// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class ArgumentGuardCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void StringGuardsDistinguishNullFromEmptyAndWhitespace(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"ArgumentGuards{optimize}{target}",
            """
            using System;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var rejected = 0;
                    try { ArgumentException.ThrowIfNullOrEmpty(null, "input"); }
                    catch (ArgumentNullException error) { if (error.ParamName == "input") rejected++; }
                    try { ArgumentException.ThrowIfNullOrWhiteSpace(null, "input"); }
                    catch (ArgumentNullException error) { if (error.ParamName == "input") rejected++; }
                    try { ArgumentException.ThrowIfNullOrEmpty("", "input"); }
                    catch (ArgumentNullException) { return -1; }
                    catch (ArgumentException error) { if (error.ParamName == "input") rejected++; }
                    foreach (var value in new[]
                    {
                        "",
                        " \t\r\n",
                        "\u0085\u00a0\u1680",
                        "\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200a\u2028\u2029\u202f\u205f\u3000",
                    })
                    {
                        try { ArgumentException.ThrowIfNullOrWhiteSpace(value, "input"); }
                        catch (ArgumentNullException) { return -2; }
                        catch (ArgumentException error) { if (error.ParamName == "input") rejected++; }
                    }
                    ArgumentException.ThrowIfNullOrEmpty(" ", "input");
                    ArgumentException.ThrowIfNullOrWhiteSpace("value", "input");
                    return rejected == 7 ? input + 1 : -3;
                }
            }
            """,
            "EntryPoint",
            optimize,
            target,
            41,
            []));

        Assert.Equal(42, result);
    }

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void EqualityGuardsUseTheGenericEqualityContract(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        ICompilationScenarioExecutor executor = new CompilationScenarioExecutor(assets);
        var result = executor.Execute(new CompilationScenario(
            $"EqualityArgumentGuards{optimize}{target}",
            """
            using System;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var first = new Token(1);
                    var same = new Token(1);
                    var different = new Token(2);

                    try
                    {
                        ArgumentOutOfRangeException.ThrowIfEqual(first, same);
                        return -1;
                    }
                    catch (ArgumentOutOfRangeException error)
                    {
                        if (error.ParamName != "first" ||
                            error.ActualValue is not Token actual ||
                            actual.Id != 1) return -2;
                    }

                    ArgumentOutOfRangeException.ThrowIfNotEqual(first, same);
                    ArgumentOutOfRangeException.ThrowIfEqual(first, different);

                    try
                    {
                        ArgumentOutOfRangeException.ThrowIfNotEqual(first, different);
                        return -3;
                    }
                    catch (ArgumentOutOfRangeException error)
                    {
                        if (error.ParamName != "first") return -4;
                    }

                    return input;
                }

                private readonly struct Token : IEquatable<Token>
                {
                    public Token(int id) => Id = id;
                    public int Id { get; }
                    public bool Equals(Token other) => Id == other.Id;
                    public override bool Equals(object? other) => false;
                    public override int GetHashCode() => Id;
                }
            }
            """,
            "EntryPoint",
            optimize,
            target,
            42,
            []));

        Assert.Equal(42, result);
    }
}
