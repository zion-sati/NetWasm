// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class GenericCatchCompilationTests
{
    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void GenericCatchClausesUseTheClosedMethodAndDeclaringTypeContext(bool optimized, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        const string source = """
            using System;
            public sealed class TaggedException<T> : Exception { }
            public static class Catcher<T> where T : Exception
            {
                public static int Match(Exception exception)
                {
                    try { throw exception; }
                    catch (T) { return 1; }
                    catch (Exception) { return 0; }
                }
            }
            public static class EntryPoint
            {
                private static int Match<T>(Exception exception) where T : Exception
                {
                    try { throw exception; }
                    catch (T) { return 1; }
                    catch (Exception) { return 0; }
                }
                private static int MatchClosed(Exception exception)
                {
                    try { throw exception; }
                    catch (TaggedException<int>) { return 1; }
                    catch (Exception) { return 0; }
                }
                private static int MatchFiltered<T>(Exception exception, bool accept) where T : Exception
                {
                    try { throw exception; }
                    catch (T) { return 1; }
                    catch (Exception) when (accept) { return 2; }
                    catch (Exception) { return 0; }
                }
                public static int Run(int input)
                {
                    var argument = new ArgumentException();
                    var operation = new InvalidOperationException();
                    var integer = new TaggedException<int>();
                    var text = new TaggedException<string>();
                    return Match<ArgumentException>(argument) == 1 &&
                        Match<ArgumentException>(operation) == 0 &&
                        Catcher<ArgumentException>.Match(argument) == 1 &&
                        Catcher<ArgumentException>.Match(operation) == 0 &&
                        Match<TaggedException<int>>(integer) == 1 &&
                        Match<TaggedException<int>>(text) == 0 &&
                        Catcher<TaggedException<int>>.Match(integer) == 1 &&
                        Catcher<TaggedException<int>>.Match(text) == 0 &&
                        MatchClosed(integer) == 1 && MatchClosed(text) == 0 &&
                        MatchFiltered<TaggedException<int>>(integer, false) == 1 &&
                        MatchFiltered<TaggedException<int>>(text, true) == 2 &&
                        MatchFiltered<TaggedException<int>>(text, false) == 0 ? input : -1;
                }
            }
            """;
        var assembly = optimized ? assets.CompileOptimizedSource("GenericCatch", source)
            : assets.CompileSource("GenericCatch", source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target));
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
        Assert.Equal(42, CompilerTestSupport.ExecuteWithStandardWasiNode(result.ApplicationModule,
            assets.Directory, 42, target, result.StaticDataEnd));
    }
}
