// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

// Array elements have no flow nodes: an element of an array of references is what the array's declared type holds
// (a call's result here), and a load through an element's address (a ??= on a cached slot) what the address points
// at, so a call on either through object takes Label's overrides, not every allocated type's (Big's and Small's,
// which only the plain ToString call reaches). The audit checks only calls whose receivers are an exact set, not
// these bounded ones, so the fixture checks their targets.
public sealed class ReceiverFlowDeclaredTypeCompilationTests
{
    private const string Template = """
        using System.Runtime.CompilerServices;

        public sealed class Big { public override string ToString() => "big"; }

        public sealed class Small { public override string ToString() => "small"; }

        public sealed class Label { public override string ToString() => "label"; }

        public static class Store
        {
            private static Label?[]? s_labels;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static Label[] Labels() => [new Label()];

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static Label Cached(int index) => (s_labels ??= new Label?[1])[index] ??= new Label();
        }

        public static class EntryPoint
        {
            public static int Run(int input)
            {
                object value = input > 0 ? new Big() : new Small();
                var text = value.ToString();
                object reflected = LOADED;
                var name = reflected.ToString();
                return text.Length * 10 + (name.Length > 0 ? 1 : 0);
            }
        }
        """;

    public static TheoryData<string, FinalWasmOptimization> Loads()
    {
        var data = new TheoryData<string, FinalWasmOptimization>();
        foreach (var load in Loaded.Keys)
        {
            data.Add(load, FinalWasmOptimization.O1);
            data.Add(load, FinalWasmOptimization.Oz);
        }
        return data;
    }

    private static readonly Dictionary<string, string> Loaded = new()
    {
        ["ArrayElement"] = "Store.Labels()[0]",
        ["ElementAddress"] = "Store.Cached(0)",
    };

    [Theory]
    [MemberData(nameof(Loads))]
    public void ALoadIsBoundedByWhatItsSourceDeclares(string load, FinalWasmOptimization optimization)
    {
        using var assets = TestAssets.Create();
        var assembly = assets.CompileSource("ReceiverFlowDeclaredTypeFixture",
            Template.Replace("LOADED", Loaded[load], StringComparison.Ordinal));
        var result = NetWasmCompiler.Compile(new CompilerOptions(
            assembly, [assets.CoreLib], "EntryPoint", "Run", [], Optimization: optimization)
        {
            ReceiverFlowAudit = true,
        });

        ReceiverFlowReflectionObjectCompilationTests.AssertOnlyItsKindsOverrides(result, assets.Directory, "Label");
    }
}
