// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: MIT

using System.Text;
using NetWasm.Runtime.Pack.Materialization;
using NetWasm.Runtime.Pack.Tests.TestSupport;

namespace NetWasm.Runtime.Pack.Tests.Materialization;

public sealed class RuntimeNativeArchiveFeatureValidatorTests
{
    [Fact]
    public void AnArchiveTheProfileAllowsPassesWithItsRequestUnchanged()
    {
        var modules = new Modules(null);
        var request = Request();

        var features = new RuntimeNativeArchiveFeatureValidator(modules, new Logs("")).Validate(request);

        Assert.Same(request.Module, modules.Request);
        Assert.False(features.UsesLegacyExceptions);
        Assert.False(features.UsesCxxExceptions);
    }

    // C++ code that throws or catches calls the C++ ABI's exception functions; code that only uses C++ does not.
    [Theory]
    [InlineData(true, "__cxa_atexit", "__cxa_throw")]
    [InlineData(true, "__cxa_begin_catch")]
    [InlineData(true, "_Unwind_CallPersonality")]
    [InlineData(false, "__cxa_atexit", "__cxa_pure_virtual", "__wasm_longjmp")]
    [InlineData(false)]
    public void AnArchiveThatThrowsOrCatchesCxxExceptionsIsReported(bool expected, params string[] imports)
    {
        var features = new RuntimeNativeArchiveFeatureValidator(new Modules(null), new Logs("", imports)).Validate(Request());

        Assert.Equal(expected, features.UsesCxxExceptions);
    }

    // Emscripten 3 and 4 emit exception handling's legacy encoding; such an archive passes on a second validation
    // that allows it, logged apart, and is reported so the final link translates it.
    [Fact]
    public void AnArchiveWithLegacyExceptionHandlingIsReported()
    {
        var modules = new Modules(new InvalidOperationException("legacy"), passesWith: "legacy-exceptions");
        var request = Request();

        var features = new RuntimeNativeArchiveFeatureValidator(modules, new Logs("")).Validate(request);

        Assert.True(features.UsesLegacyExceptions);
        Assert.Equal(2, modules.Requests.Count);
        Assert.Same(request.Module, modules.Requests[0]);
        Assert.Equal([.. request.Module.Profile.Features, "legacy-exceptions"], modules.Request!.Profile.Features);
        Assert.Equal("/logs/native-archive-features-0.log.legacy-exceptions.log", modules.Request.LogPath);
    }

    // A profile that already allows the legacy encoding has nothing to retry.
    [Fact]
    public void AProfileAllowingLegacyExceptionHandlingValidatesOnce()
    {
        var failure = new InvalidOperationException("tool failed");
        var modules = new Modules(failure);
        var request = Request();
        request = request with
        {
            Module = request.Module with
            {
                Profile = request.Module.Profile with { Features = request.Module.Profile.Features.Add("legacy-exceptions") },
            },
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeNativeArchiveFeatureValidator(modules, new Logs("")).Validate(request));

        Assert.Same(failure, error);
        Assert.Single(modules.Requests);
    }

    // wasm-tools' reason follows "Caused by:", numbered.
    [Theory]
    [InlineData("error: func 11 failed to validate\n\nCaused by:\n    0: tail calls support is not enabled (at offset 0x103b)\n",
        "tail calls support is not enabled (at offset 0x103b)")]
    [InlineData("error: func 3 failed to validate\nCaused by:\n  threads support is not enabled\n", "threads support is not enabled")]
    [InlineData("error: input is not a module\n", "error: input is not a module")]
    [InlineData("", "the validator gave no reason")]
    public void AnArchiveBeyondTheProfileNamesTheLibraryTheProfileAndTheReason(string log, string reason)
    {
        var failure = new InvalidOperationException("A NetWasm runtime tool failed; see 'native-archive-features-0.log'.");

        var error = Assert.Throws<InvalidOperationException>(() =>
            new RuntimeNativeArchiveFeatureValidator(new Modules(failure), new Logs(log)).Validate(Request()));

        Assert.Equal(
            "Native library 'zng' uses WebAssembly beyond NetWasm's static native profile " +
            $"({string.Join(", ", RuntimePackTestData.NativeProfile().Features)}): {reason}. Build it without that feature.",
            error.Message);
        Assert.Same(failure, error.InnerException);
    }

    [Fact]
    public void TheValidatorRequiresItsCollaboratorsAndARequest()
    {
        Assert.Throws<ArgumentNullException>(() => new RuntimeNativeArchiveFeatureValidator(null!, new Logs("")));
        Assert.Throws<ArgumentNullException>(() => new RuntimeNativeArchiveFeatureValidator(new Modules(null), null!));
        var validator = new RuntimeNativeArchiveFeatureValidator(new Modules(null), new Logs(""));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
        Assert.Throws<ArgumentException>(() => validator.Validate(Request() with { LibraryName = " " }));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(Request() with { Module = null! }));
    }

    private static RuntimeNativeArchiveFeatureValidationRequest Request() => new("zng", new(
        RuntimePackTestData.NativeProfile(), "wasm32", "/work/archive-0.o", "node", "run.mjs", "wasm-tools.wasm",
        "/logs/native-archive-features-0.log"));

    private sealed class Modules(InvalidOperationException? failure, string? passesWith = null) : IRuntimeNativeModuleValidator
    {
        public List<RuntimeNativeModuleValidationRequest> Requests { get; } = [];

        public RuntimeNativeModuleValidationRequest? Request => Requests.LastOrDefault();

        public void Validate(RuntimeNativeModuleValidationRequest request)
        {
            Requests.Add(request);
            if (failure is not null && (passesWith is null || !request.Profile.Features.Contains(passesWith))) throw failure;
        }
    }

    // The validation log, and the archive's relocatable object with its function imports.
    private sealed class Logs(string text, params string[] imports) : IRuntimeArtifactReader
    {
        public byte[] Read(string path)
        {
            if (path == "/work/archive-0.o")
                return ObjectImporting(imports);
            Assert.Equal("/logs/native-archive-features-0.log", path);
            return Encoding.UTF8.GetBytes(text);
        }
    }

    private static byte[] ObjectImporting(string[] names)
    {
        var section = new List<byte> { (byte)names.Length };
        foreach (var name in names)
        {
            section.AddRange([3, (byte)'e', (byte)'n', (byte)'v', (byte)name.Length, .. Encoding.UTF8.GetBytes(name), 0, 0]);
        }
        return [0, 0x61, 0x73, 0x6d, 1, 0, 0, 0, 1, 4, 1, 0x60, 0, 0, 2, (byte)section.Count, .. section];
    }
}
