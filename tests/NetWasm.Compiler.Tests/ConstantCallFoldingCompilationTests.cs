// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.ComponentModel;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

using static CompilerTestSupport;

public sealed class ConstantCallFoldingCompilationTests
{
    private const string Source = """
        using System;
        using System.Runtime.CompilerServices;
        public static class Initialized
        {
            public static readonly string Role = "Admin";
            static Initialized() { EntryPoint.Initializations++; }
            public static bool IsAdmin(string role) => role == "Admin";
        }
        public static class EntryPoint
        {
            public static int Initializations;
            private static bool IsAdmin(string role) => role == "Admin";
            private static bool IsNotGuest(string? role) => role != "Guest";
            private static bool IsPng(ReadOnlySpan<byte> header) => header.SequenceEqual("PNG"u8);
            private static bool SameText(string? left, string? right) => string.Equals(left, right);
            private static bool InstanceEquals(string left, string? right) => left.Equals(right);
            private static bool SameObject(object left, object right) => left == right;
            [MethodImpl(MethodImplOptions.NoInlining)]
            private static bool KeptAdmin(string role) => role == "Admin";
            public static int Run(int input)
            {
                var score = 0;
                if (IsAdmin("Admin")) score += 1;
                if (!IsAdmin("Guest")) score += 2;
                if (IsNotGuest(null)) score += 4;
                if (!IsPng("GIF"u8)) score += 8;
                if (IsPng("PNG"u8)) score += 16;
                if (SameText(null, null)) score += 32;
                if (!SameText("a", null)) score += 64;
                if (!InstanceEquals("a", null)) score += 128;
                if (InstanceEquals("abc", "abc")) score += 256;
                // Values known only at run time keep their comparison.
                if (IsAdmin(input == 1 ? "Admin" : "Guest")) score += 512;
                // Distinct literals are distinct objects; equal ones follow interning at run time.
                if (!SameObject("a", "b")) score += 1024;
                if (SameObject("a", "a")) score += 2048;
                if (KeptAdmin("Admin") && Initialized.IsAdmin("Admin") && Initializations == 1) score += 4096;
                return score;
            }
        }
        """;

    private static readonly string[] Folded =
        ["EntryPoint::IsNotGuest", "EntryPoint::IsPng", "EntryPoint::SameText", "EntryPoint::InstanceEquals"];

    [Theory]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.None)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.None)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.O3)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.O3)]
    [InlineData(false, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(false, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm32, FinalWasmOptimization.Oz)]
    [InlineData(true, WasmTarget.Wasm64, FinalWasmOptimization.Oz)]
    public void FoldsConstantStringAndUtf8ComparisonsThroughHelpers(bool optimize, WasmTarget target, FinalWasmOptimization preset)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("ConstantComparisons", Source) : assets.CompileSource("ConstantComparisons", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: preset));
        var methods = Names(result);

        // None keeps every call; the whole-program presets fold the constant ones away.
        Assert.Equal(preset == FinalWasmOptimization.None ? Folded : [], Folded.Where(methods.Contains));
        // O3 and Oz inline the helpers that do not fold (Oz as callees of at most 16 IL bytes); NoInlining keeps its
        // call everywhere.
        Assert.Equal(preset == FinalWasmOptimization.None, methods.Contains("EntryPoint::IsAdmin"));
        Assert.Equal(preset == FinalWasmOptimization.None, methods.Contains("EntryPoint::SameObject"));
        Assert.Contains("EntryPoint::KeptAdmin", methods);
        // A precise static constructor must still run, so that call is neither folded nor inlined.
        Assert.Contains("Initialized::IsAdmin", methods);
        Assert.Equal(7679, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
        Assert.Equal(8191, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 1, target, result.StaticDataEnd));
    }

    private static HashSet<string> Names(CompilationResult result) =>
        [.. result.Program.Methods.Values.Select(body => $"{body.Method.DeclaringType.FullName}::{body.Method.Definition.Name}")];

    [Theory]
    [InlineData(false, WasmTarget.Wasm32)]
    [InlineData(false, WasmTarget.Wasm64)]
    [InlineData(true, WasmTarget.Wasm32)]
    [InlineData(true, WasmTarget.Wasm64)]
    public void DisablingTheFlagKeepsTheCalls(bool optimize, WasmTarget target)
    {
        using var assets = TestAssets.Create();
        var assembly = optimize ? assets.CompileOptimizedSource("ConstantComparisonsKept", Source) : assets.CompileSource("ConstantComparisonsKept", Source);
        var result = NetWasmCompiler.Compile(new CompilerOptions(assembly, [assets.CoreLib], "EntryPoint", "Run", [],
            Target: target, Optimization: FinalWasmOptimization.Oz,
            // Inlining off too, so only folding could remove the calls.
            OptimizationOverrides: new(ConstantCallFolding: ManagedConstantCallFolding.Disabled,
                ManagedMethodInlining: ManagedMethodInlining.Disabled)));
        var methods = Names(result);

        Assert.All(Folded, name => Assert.Contains(name, methods));
        Assert.Equal(7679, ExecuteWithStandardWasiNode(result.ApplicationModule, assets.Directory, 0, target, result.StaticDataEnd));
    }
}
