using System.Security.Cryptography;
using NetWasm.Compiler.Wasm.Emission;
using NetWasm.Compiler.Wasm.Emission.NativeInterop;

namespace NetWasm.Compiler.Wasm.Tests;

public sealed class NativeCallbackSupportArtifactValidatorTests
{
    [Fact]
    public void ValidatorAcceptsAbsentAndConsistentArtifacts()
    {
        var validator = Assert.IsAssignableFrom<INativeCallbackSupportArtifactValidator>(
            new NativeCallbackSupportArtifactValidator());

        validator.Validate(null);
        validator.Validate(CreateArtifact());
    }

    [Fact]
    public void ValidatorRejectsDigestAndDescriptorTypeCorruption()
    {
        var validator = Assert.IsAssignableFrom<INativeCallbackSupportArtifactValidator>(
            new NativeCallbackSupportArtifactValidator());
        var valid = CreateArtifact();

        Assert.Throws<InvalidOperationException>(() => validator.Validate(
            valid with { Sha256 = new string('0', 64) }));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(
            valid with
            {
                Callbacks = [valid.Callbacks[0] with
                {
                    Parameters = [(WasmValueType)0],
                }],
            }));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(
            valid with
            {
                Callbacks = [valid.Callbacks[0] with
                {
                    ReturnType = (WasmValueType)0,
                }],
            }));
    }

    [Fact]
    public void ValidatorAcceptsNamedOnlyArtifactWithoutTemporaryExports()
    {
        var valid = CreateArtifact();

        new NativeCallbackSupportArtifactValidator().Validate(valid with
        {
            Callbacks = [valid.Callbacks[0] with
            {
                NativeSymbol = "named_callback",
                RuntimeImportSymbol = "named_callback_import",
                ApplicationExportName = "named_callback",
                RuntimeGetterExportName = null,
            }],
            TemporaryApplicationExports = [],
            TemporaryRuntimeExports = [],
        });
    }

    // Issue 226: an object may define only unmanaged calli trampolines, which follow the getters
    // among the temporary runtime exports.
    [Fact]
    public void ValidatorBindsTrampolinesAfterTheGetters()
    {
        var validator = new NativeCallbackSupportArtifactValidator();
        var valid = CreateArtifact();
        var getters = valid.TemporaryRuntimeExports;

        validator.Validate(valid with
        {
            TemporaryRuntimeExports = [.. getters, "netwasm_calli_ii"],
            Trampolines = ["netwasm_calli_ii"],
        });
        validator.Validate(valid with
        {
            Callbacks = [],
            TemporaryApplicationExports = [],
            TemporaryRuntimeExports = ["netwasm_calli_ii"],
            Trampolines = ["netwasm_calli_ii"],
        });
        foreach (var invalid in new[]
        {
            valid with { Callbacks = [], TemporaryApplicationExports = [], TemporaryRuntimeExports = [] },
            valid with { Trampolines = default },
            valid with { Trampolines = ["netwasm_calli_ii"] },
            valid with { TemporaryRuntimeExports = ["netwasm_calli_ii", .. getters], Trampolines = ["netwasm_calli_ii"] },
            valid with { TemporaryRuntimeExports = [.. getters, "bad\0name"], Trampolines = ["bad\0name"] },
            valid with { TemporaryRuntimeExports = [.. getters, .. getters], Trampolines = getters },
        })
        {
            Assert.Throws<InvalidOperationException>(() => validator.Validate(invalid));
        }
    }

    [Fact]
    public void ValidatorRejectsDuplicateApplicationAndLinkerSymbols()
    {
        var validator = new NativeCallbackSupportArtifactValidator();
        var valid = CreateArtifact();
        var first = valid.Callbacks[0];

        Assert.Throws<InvalidOperationException>(() => validator.Validate(valid with
        {
            Callbacks = [first, first with
            {
                NativeSymbol = "native_callback_2",
                ApplicationExportName = "application_callback_2",
                RuntimeGetterExportName = "callback_address_2",
            }],
            TemporaryApplicationExports =
                ["application_callback", "application_callback_2"],
            TemporaryRuntimeExports = ["callback_address", "callback_address_2"],
        }));
        Assert.Throws<InvalidOperationException>(() => validator.Validate(valid with
        {
            Callbacks = [first, first with
            {
                NativeSymbol = "native_callback_2",
                RuntimeImportSymbol = "runtime_callback_2",
                RuntimeGetterExportName = "callback_address_2",
            }],
            TemporaryApplicationExports =
                ["application_callback", "application_callback"],
            TemporaryRuntimeExports = ["callback_address", "callback_address_2"],
        }));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("bad\nname")]
    public void ValidatorRejectsNonPortableSymbolNames(string symbol)
    {
        var valid = CreateArtifact();

        Assert.Throws<InvalidOperationException>(() =>
            new NativeCallbackSupportArtifactValidator().Validate(valid with
            {
                Callbacks = [valid.Callbacks[0] with { NativeSymbol = symbol }],
            }));
    }

    private static WasmNativeCallbackSupportArtifact CreateArtifact()
    {
        var bytes = new byte[] { 0, 97, 115, 109, 1, 0, 0, 0 };
        return new(
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            [new(
                "native_callback",
                "runtime_callback",
                "application_callback",
                "callback_address",
                [WasmValueType.I32, WasmValueType.I64],
                WasmValueType.I32)],
            ["application_callback"],
            ["callback_address"]);
    }
}
