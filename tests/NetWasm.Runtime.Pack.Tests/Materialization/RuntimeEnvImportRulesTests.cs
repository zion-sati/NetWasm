// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeEnvImportRulesTests
{
    private static readonly HashSet<string> Modules = ["webgl", "gl2"];
    private static readonly HashSet<string> Runtime = ["emscripten_notify_memory_growth"];

    [Fact]
    public void AcceptsDeclaredFunctionsOfRawOutputInNameOrder()
    {
        var imports = RuntimeEnvImportRules.Validate(
            [new("glViewport", "gl2"), new("glClear", "webgl"), new("_emscripten$x", "webgl")], true, Modules, Runtime);

        Assert.Equal(["_emscripten$x", "glClear", "glViewport"], imports.Select(import => import.Name));
        Assert.Empty(RuntimeEnvImportRules.Validate(default, false, null, Runtime));
        Assert.Empty(RuntimeEnvImportRules.Validate([], false, null, Runtime));
    }

    // Each import is "name=module", separated by ";".
    public static TheoryData<string, bool, string> Invalid => new()
    {
        { "glClear=webgl", false, "A component has no JavaScript" },
        { "gl Clear=webgl", true, "is not a C function name" },
        { "1gl=webgl", true, "is not a C function name" },
        { "=webgl", true, "is not a C function name" },
        { "glClear=webgl;glClear=gl2", true, "is declared twice" },
        { "emscripten_notify_memory_growth=webgl", true, "the NetWasm runtime's own env functions" },
        { "glClear=", true, "needs Module" },
        { "glClear=env", true, "needs Module" },
        { "glClear=netwasm.runtime.v1", true, "needs Module" },
        { "glClear=wasi:io/streams", true, "needs Module" },
        { "glClear=a|b", true, "needs Module" },
        { "glClear=opengl", true, "Declare <NetWasmImportModule Include=\"opengl\" />" },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void RejectsWhatTheJavaScriptCannotSupply(string imports, bool rawWasmOutput, string message)
    {
        var parsed = imports.Split(';').Select(import => import.Split('=')).Select(parts => new RuntimeEnvImport(parts[0], parts[1]));
        var error = Assert.Throws<InvalidOperationException>(() =>
            RuntimeEnvImportRules.Validate([.. parsed], rawWasmOutput, Modules, Runtime));

        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RequiresDeclaredModulesAndTheRuntimesFunctions()
    {
        Assert.Throws<InvalidOperationException>(() =>
            RuntimeEnvImportRules.Validate([new("glClear", "webgl")], true, null, Runtime));
        Assert.Throws<ArgumentNullException>(() =>
            RuntimeEnvImportRules.Validate([new("glClear", "webgl")], true, Modules, null!));
    }
}
