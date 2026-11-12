# Timezone assets

NetWasm defaults to UTC and does not embed timezone data in CoreLib or the
application Wasm module. Applications that expose reachable local-time APIs and
set `TZ` to an IANA identifier must explicitly deploy a matching timezone asset.

## Choose the zones a deployment ships

List the zones in the project file:

```xml
<ItemGroup>
  <NetWasmTimeZone Include="Australia/Melbourne;America/New_York" />
</ItemGroup>
```

Aliases resolve to their zones automatically, so `Asia/Calcutta` ships with
`Asia/Kolkata`. To ship every zone instead, set
`<NetWasmTimeZoneMode>Full</NetWasmTimeZoneMode>` and list none.

Build, `dotnet run` and every publish (local, browser, `JsModule` and WIT
workers) then write `<program>.wasm.tz-info` beside the primary Wasm and record
it in the deployment manifest. The zones come from the IANA release pinned by
NetWasm.Toolchain, so every machine produces the same sidecar. The sidecar is
written only when the application reaches a local-time API. Otherwise the build
warns with NWSDK080 and ships nothing. An unknown zone, or `Full` combined with
listed zones, fails with NWSDK079. Any `NetWasmTimeZoneMode` other than
`Selected` or `Full` fails with NWSDK083, and `Selected` without a
`NetWasmTimeZone` item fails with NWSDK084.

`TZ` remains an explicit execution setting; NetWasm never infers a host or
browser zone. Set it with a `NetWasmEnvironmentVariable` item, in the execution
request's environment, or per instance with `open({ environment: { TZ } })` for a
page-hosted `JsModule`. A page that wants the user's zone passes
`Intl.DateTimeFormat().resolvedOptions().timeZone`, so its sidecar must include
that zone. A `TZ` outside the sidecar fails before managed code starts.

One zone with daylight saving time is about 210 KB (about 51 KB with Brotli).
Each alias is stored as another full copy of its zone, which Brotli removes
almost entirely: `Australia/Melbourne` and `America/New_York` ship with three
aliases (`Australia/Victoria`, `US/Eastern` and `EST5EDT`) as about 1 MB, or
about 100 KB with Brotli. `Full` is about 40.7 MB, or 1.8 MB with Brotli. Static
hosts that cannot serve the Brotli form send the full size.

## Generate an asset directly

The SDK uses the same generator as the standalone tool, which remains available
for custom hosts:

```sh
dotnet run --project tools/NetWasm.TimeZones -- generate \
  --source /path/to/pinned/zoneinfo \
  --asset dist/netwasm-timezones.nwtz \
  --brotli dist/netwasm-timezones.nwtz.br \
  --manifest dist/netwasm-timezones.json \
  --browser-loader dist/netwasm-timezones.loader.mjs \
  --zone Australia/Melbourne
```

Repeat `--zone` for an explicit application-specific selection. Required IANA
aliases are included automatically. Use `--all` instead of `--zone` to publish
the convenient official full-database sidecar. There is intentionally no
subjective “common zones” preset.

The source directory must contain compiled TZif files and an authoritative IANA
version in `version`, `+VERSION`, or the first line of `tzdata.zi`. The generator
retains historical transitions and materializes the TZif future rule through the
full `DateTime` range. Its outputs are deterministic for the same source:

- `netwasm-timezones.nwtz`: the uncompressed indexed payload mounted through
  WASI;
- `netwasm-timezones.nwtz.br`: the transfer/deployment form;
- `netwasm-timezones.json`: the IANA version, payload identity, exact sizes, and
  included zone names;
- `netwasm-timezones.loader.mjs`: browser configuration of the WASI 0.3
  environment and filesystem for a custom host that runs the component through
  jco and preview3-shim.

For browsers, serve the Brotli representation at the asset URL with
`Content-Encoding: br`. `fetch()` receives the decompressed bytes. NetWasm's
browser hosts mount them themselves. The program opens its time zone data
read-only as `/netwasm-sidecars/<program>.wasm.tz-info`, under the name the
build gave it (see [program sidecars](diagnostics.md#program-sidecars)). In a
custom host that instantiates the component through jco, the generated loader
mounts those bytes there before the component starts; pass the program's file
name as `programFileName`, for example
`configureNetWasmTimeZone({ timeZone, programFileName: "App.wasm" })`. The
loader configures `@bytecodealliance/preview3-shim`'s browser environment and
preopens; it does not add a framework `JSImport`. jco 1.41 maps only
`wasi:*@0.3.0` imports to preview3-shim by default, so such a host passes a
`map` for the component's `@0.3.2` imports. Browser support in preview3-shim is
recent, so keep a browser test of the host when upgrading it.

Invoke the generated loader only for a component whose imports show reachable
local-time support. Passing no `timeZone`, `UTC`, or `Etc/UTC` clears timezone
preopens and performs no asset fetch. Any other identifier configures `TZ`,
requires `programFileName`, fetches the asset, and fails immediately if the
response is unsuccessful.

Native hosts decompress the `.br` deployment artifact before startup and
preopen a read-only directory at `/netwasm-sidecars` containing the
uncompressed payload as `<program>.wasm.tz-info`. Under Wasmtime, mount the
folder that holds the program:
`wasmtime run --env TZ=Australia/Melbourne --dir bin::/netwasm-sidecars bin/App.wasm`. Both native and browser lanes therefore expose the same
standard `wasi:filesystem` contract to managed code.

Hosts check the payload's SHA-256 digest against the deployment manifest before
mounting it: browser hosts with Web Crypto, the local launcher before it
preopens the directory. A custom host that mounts a payload from the standalone
generator should verify it the same way, because the guest trusts the mounted
file. Bootstrap then validates the requested identifier, payload format, pinned
IANA version, and selected-zone membership before the managed entry point runs.
It reads only the zone index and the selected zone; other zones are read and
validated when `FindSystemTimeZoneById` or `GetSystemTimeZones` first needs
them. Invalid `TZ`, missing capabilities, missing files, incompatible data, and
absent zones fail loudly with a deterministic diagnostic; they never fall back
to UTC.
