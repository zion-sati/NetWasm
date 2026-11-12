using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.TestSupport;

internal static class RuntimePackTestData
{
    public const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static RuntimePackManifest Manifest()
    {
        var targets = ImmutableArray.Create(Target("wasm32"), Target("wasm64"),
            Target("wasm32", "NetWasm"), Target("wasm64", "NetWasm"),
            Target("wasm32", "Bump"), Target("wasm64", "Bump"));
        return new(
        6,
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
        targets)
        {
            DefaultGarbageCollector = "Compact",
            InlineIntents = [new("initialize", RuntimeFunctionInlinePreference.Aggressive)],
            TargetLookup = targets.ToImmutableDictionary(target => (target.Target, target.GarbageCollector)),
        };
    }

    public static RuntimePackTarget Target(string target, string collector = "Compact") => target == "wasm64"
        ? new(
            target,
            8,
            16,
            114_352,
            65_536,
            65_536,
            8_589_934_592,
            8_589_934_592,
            Asset($"wasm64/{collector.ToLowerInvariant()}/libnetwasm-runtime.a"),
            Asset($"wasm64/{collector.ToLowerInvariant()}/libgc.a"),
            Asset("wasm64/allowed-undefined-symbols.txt"),
            new(["libc.a"], [Asset("wasm64/system-libraries/libc.a")]))
        {
            GarbageCollector = collector,
            NativeValidation = NativeProfile(target),
            Speed = SpeedAssets(target, collector, 114_352),
            PlatformArchives = PlatformArchives($"{target}/{collector.ToLowerInvariant()}"),
        }
        : new(
            target,
            4,
            16,
            91_968,
            65_536,
            65_536,
            2_147_483_648,
            2_147_483_648,
            Asset($"wasm32/{collector.ToLowerInvariant()}/libnetwasm-runtime.a"),
            Asset($"wasm32/{collector.ToLowerInvariant()}/libgc.a"),
            Asset("wasm32/allowed-undefined-symbols.txt"),
            new(["libc.a"], [Asset("wasm32/system-libraries/libc.a")]))
        {
            GarbageCollector = collector,
            NativeValidation = NativeProfile(target),
            Speed = SpeedAssets(target, collector, 91_968),
            PlatformArchives = PlatformArchives($"{target}/{collector.ToLowerInvariant()}"),
        };

    public static RuntimePackSpeedAssets SpeedAssets(string target, string collector, long footprint) => new(
        footprint,
        Asset($"{target}/{collector.ToLowerInvariant()}/speed/libnetwasm-runtime.a"),
        Asset($"{target}/{collector.ToLowerInvariant()}/speed/libgc.a"))
    {
        PlatformArchives = PlatformArchives($"{target}/{collector.ToLowerInvariant()}/speed"),
    };

    public static RuntimePackPlatformArchives PlatformArchives(string directory) => new(
        Asset($"{directory}/libnetwasm-runtime-wasi.a"),
        Asset($"{directory}/libnetwasm-runtime-nowasi.a"));

    public static RuntimeNativeValidationProfile NativeProfile(string target = "wasm32") => new(1,
        ["mvp", "mutable-global", "saturating-float-to-int", "sign-extension", "reference-types", "multi-value", "bulk-memory",
            "simd", .. target == "wasm64" ? ImmutableArray.Create("memory64") : []],
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
        ["__netwasm_callback_address_0"],
        []);

    public static RuntimeMemoryLayout Layout(string target = "wasm32") => target == "wasm64"
        ? new(65_552, 179_904, 262_144, 8_589_934_592)
        : new(65_552, 157_520, 262_144, 2_147_483_648);

    public static RuntimeLinkMemoryLimits LinkLimits(string target = "wasm32")
    {
        var layout = Layout(target);
        return new(layout.RuntimeGlobalBase, layout.InitialMemorySizeBytes, layout.MaximumMemorySizeBytes);
    }
}
