// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Tests;

public sealed class DiagnosticOutputCompilationTests
{
    [Theory]
    [InlineData(WasmTarget.Wasm32, false)]
    [InlineData(WasmTarget.Wasm32, true)]
    [InlineData(WasmTarget.Wasm64, false)]
    [InlineData(WasmTarget.Wasm64, true)]
    public void ConditionalCallsAndArgumentEvaluationFollowTheConsumerBuild(WasmTarget target, bool debug)
    {
        using var assets = TestAssets.Create();
        var source = File.ReadAllText(Path.Combine(assets.Root, "tests", "end-to-end", "diagnostic-output", "Program.cs"));
        source += "\npublic static class EntryPoint { public static int Run(int input) => Program.Main(); }";
        var assembly = assets.CompileSource("DiagnosticOutput", (debug ? "#define DEBUG\n" : "") + source);
        var result = NetWasmCompiler.Compile(new(assembly, [assets.CoreLib], "EntryPoint", "Run", [], Target: target,
            WitPath: Path.Combine(assets.Root, "wit", "netwasm-platform-1.0.0"),
            WitWorld: "netwasm:platform/platform@1.0.0"));
        var methods = result.Program.Methods.Values.Concat(result.Program.ConstructedMethods.Values)
            .Select(body => body.Method).ToArray();
        var output = methods.Where(method => method.DeclaringType.FullName == "System.Diagnostics.Debug").ToArray();
        if (debug)
        {
            // Upstream Debug routes through its static DebugProvider, which reads the indentation. The overloads that
            // only forward to another, and the indentation accessors, may be inlined into their callers.
            Assert.Contains(output, method => method.Definition.Name == "WriteLine");
            Assert.Subset(new HashSet<string>(StringComparer.Ordinal) { ".cctor", "get_IndentLevel", "get_IndentSize", "WriteLine" },
                output.Select(method => method.Definition.Name).ToHashSet(StringComparer.Ordinal));
            Assert.Contains(methods, method => method.DeclaringType.FullName == "System.Diagnostics.DebugProvider" &&
                method.Definition.Name == "GetIndentString");
            Assert.Contains(methods, method => method.DeclaringType.FullName == "Program" && method.Definition.Name == "Evaluate");
            // DebugProvider writes to standard error; Console.Error is an accessor that may be inlined, while the
            // standard error writer's initializer is a method of its own.
            Assert.Contains(methods, IsStandardErrorInitializer);
        }
        else
        {
            Assert.Empty(output);
            Assert.DoesNotContain(methods, method => method.DeclaringType.FullName == "Program" && method.Definition.Name == "Evaluate");
            Assert.DoesNotContain(methods, IsStandardErrorInitializer);
        }
        CompilerTestSupport.ValidateWithNode(result.ApplicationModule, assets.Directory);
    }

    private static bool IsStandardErrorInitializer(MethodInstanceModel method) =>
        method.Definition.Name == ".cctor" &&
        method.DeclaringType.FullName?.StartsWith("System.Console", StringComparison.Ordinal) == true &&
        method.DeclaringType.FullName.EndsWith("StandardError", StringComparison.Ordinal);
}
