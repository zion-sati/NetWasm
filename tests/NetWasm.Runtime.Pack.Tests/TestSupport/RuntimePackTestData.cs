using System.Collections.Immutable;
using NetWasm.Runtime.Pack.Materialization;

namespace NetWasm.Runtime.Pack.Tests.TestSupport;

internal static class RuntimePackTestData
{
    public const string Digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static RuntimePackManifest Manifest() => new(
        1,
        "netwasm.runtime.v1",
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
            [Asset("wasm64/system/libc.a")])
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
            [Asset("wasm32/system/libc.a")]);

    public static RuntimePackAsset Asset(string path) => new(path, Digest);

    public static RuntimeMemoryLayout Layout(string target = "wasm32") => target == "wasm64"
        ? new(65_552, 179_904, 262_144, 8_589_934_592)
        : new(65_552, 157_520, 262_144, 2_147_483_648);
}
