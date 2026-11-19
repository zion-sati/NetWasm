// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeExceptionHandlingTranslatorTests
{
    // Binaryen rewrites the module in place, reading the legacy encoding with the profile's other features and keeping
    // names for the later passes.
    [Fact]
    public void TranslatesTheModuleInPlaceWithTheProfilesFeatures()
    {
        var commands = new Commands();

        new RuntimeExceptionHandlingTranslator(commands).Translate(new(
            "/tools/wasm-opt", "/work/runtime.wasm", ["mvp", "simd", "exceptions"], "/logs/native-exnref.log"));

        var command = Assert.Single(commands.Invoked);
        Assert.Equal("/tools/wasm-opt", command.ExecutablePath);
        Assert.Equal(
            ["/work/runtime.wasm", "--translate-to-exnref", "--mvp-features", "--enable-exception-handling", "--enable-simd",
                "-g", "-o", "/work/runtime.wasm"],
            command.Arguments);
        Assert.Equal("/logs/native-exnref.log", command.LogPath);
    }

    [Fact]
    public void RejectsAnIncompleteRequest()
    {
        Assert.Throws<ArgumentNullException>(() => new RuntimeExceptionHandlingTranslator(null!));
        var translator = new RuntimeExceptionHandlingTranslator(new Commands());
        RuntimeExceptionHandlingTranslationRequest valid = new("/tools/wasm-opt", "/work/runtime.wasm", ["mvp"], "/logs/x.log");
        Assert.Throws<ArgumentNullException>(() => translator.Translate(null!));
        Assert.Throws<ArgumentException>(() => translator.Translate(valid with { WasmOptPath = " " }));
        Assert.Throws<ArgumentException>(() => translator.Translate(valid with { ModulePath = "" }));
        Assert.Throws<ArgumentException>(() => translator.Translate(valid with { LogPath = " " }));
        Assert.Throws<InvalidOperationException>(() => translator.Translate(valid with { ModulePath = "runtime.wasm" }));
        Assert.Throws<InvalidOperationException>(() => translator.Translate(valid with { Features = ["simd"] }));
    }

    private sealed class Commands : ICommandInvoker
    {
        public List<RuntimeCommand> Invoked { get; } = [];

        public void Invoke(RuntimeCommand command) => Invoked.Add(command);
    }
}
