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

        new RuntimeNativeArchiveFeatureValidator(modules, new Logs("")).Validate(request);

        Assert.Same(request.Module, modules.Request);
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

    private sealed class Modules(InvalidOperationException? failure) : IRuntimeNativeModuleValidator
    {
        public RuntimeNativeModuleValidationRequest? Request { get; private set; }

        public void Validate(RuntimeNativeModuleValidationRequest request)
        {
            Request = request;
            if (failure is not null) throw failure;
        }
    }

    private sealed class Logs(string text) : IRuntimeArtifactReader
    {
        public byte[] Read(string path)
        {
            Assert.Equal("/logs/native-archive-features-0.log", path);
            return Encoding.UTF8.GetBytes(text);
        }
    }
}
