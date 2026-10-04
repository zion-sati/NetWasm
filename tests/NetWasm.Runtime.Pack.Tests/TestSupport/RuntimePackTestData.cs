using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.TestSupport;

internal static class RuntimePackTestData
{
    public const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static RuntimePackManifest Manifest() => new(
        4,
        "netwasm.runtime.v1",
        "6.0.7",
        65_536,
        ["initialize", "allocate"],
        new(
            "eng/build-netwasm-runtime.sh",
            "release",
            "eng/toolchain.json",
            "fingerprint",
            "runtime sources",
            "per-application wasm-ld final link"),
        [Target("wasm32"), Target("wasm64")]);

    public static RuntimePackTarget Target(string target) => target == "wasm64"
        ? new(
            target,
            8,
            16,
            114_352,
            65_536,
            65_536,
            8_589_934_592,
            8_589_934_592,
            Asset("wasm64/libnetwasm-runtime.a"),
            Asset("wasm64/libgc.a"),
            Asset("wasm64/allowed-undefined-symbols.txt"),
            new(["libc.a"], [Asset("wasm64/system-libraries/libc.a")])) { NativeValidation = NativeProfile(target) }
        : new(
            target,
            4,
            16,
            91_968,
            65_536,
            65_536,
            2_147_483_648,
            2_147_483_648,
            Asset("wasm32/libnetwasm-runtime.a"),
            Asset("wasm32/libgc.a"),
            Asset("wasm32/allowed-undefined-symbols.txt"),
            new(["libc.a"], [Asset("wasm32/system-libraries/libc.a")])) { NativeValidation = NativeProfile(target) };

    public static RuntimeNativeValidationProfile NativeProfile(string target = "wasm32") => new(1,
        ["mvp", "mutable-global", "saturating-float-to-int", "sign-extension", "reference-types", "multi-value", "bulk-memory",
            .. target == "wasm64" ? ImmutableArray.Create("memory64") : []],
        [new("env", "emscripten_notify_memory_growth", [target == "wasm64" ? (byte)0x7e : (byte)0x7f], [], true)]);

    public static RuntimePackAsset Asset(string path) => new(path, Digest);

    public static RuntimeNativeCallbackSupport CallbackSupport(
        string target = "wasm32",
        string digest = Digest) => new(
        "application.callbacks.o",
        digest,
        [new(
            "__netwasm_native_callback_0",
            "__netwasm_native_callback_0",
            "__netwasm_application_callback_0",
            "__netwasm_callback_address_0",
            [RuntimeNativeValueType.I32,
                target == "wasm64"
                    ? RuntimeNativeValueType.I64
                    : RuntimeNativeValueType.I32],
            RuntimeNativeValueType.I32)],
        ["__netwasm_application_callback_0"],
        ["__netwasm_callback_address_0"]);

    public static RuntimeMemoryLayout Layout(string target = "wasm32") => target == "wasm64"
        ? new(65_552, 179_904, 262_144, 8_589_934_592)
        : new(65_552, 157_520, 262_144, 2_147_483_648);

    public static RuntimeLinkMemoryLimits LinkLimits(string target = "wasm32")
    {
        var layout = Layout(target);
        return new(layout.RuntimeGlobalBase, layout.InitialMemorySizeBytes, layout.MaximumMemorySizeBytes);
    }
}
