# Public TimeZoneInfo qualification fixture

This fixture is the T01 semantic corpus for the public `System.TimeZoneInfo`
facade. It intentionally uses the existing versioned NetWasm timezone service;
it does not introduce a second database or a JavaScript import path.

The ordinary corpus requires a deployment-selected asset containing at least:

- `Australia/Melbourne`
- `America/New_York`
- the UTC identity (`UTC`)

The selected asset must retain transition history through 1900. The normal
acceptance test compares the same source under desktop .NET and NetWasm for
Debug/Release CIL and wasm32/wasm64, asserting UTC identity and lookup, fixed
and daylight offsets, ambiguous and invalid local times, conversion round trips,
historical offsets, equality/hash behavior, invalid identifiers, local-zone
access, and selected-zone enumeration.

The negative entry points are intentionally separate from the desktop oracle:
desktop .NET uses its host timezone database and cannot model an absent or
corrupt NetWasm asset. The WASI/native and browser host adapters must set
`TZ=Australia/Melbourne`, run `RunMissingAsset` without the asset, and run
`RunCorruptAsset` with a digest-invalid asset. Both cases must produce a
managed `PlatformNotSupportedException` and must not silently fall back to UTC
or the host timezone.

The qualification test supplies `timeZoneAssetPath` and the `TZ` environment
entry through the existing Node/WASI oracle boundary. The selected asset path
comes from `NETWASM_TIMEZONE_ASSET_PATH`; this keeps deployment data outside
the repository and lets native and browser adapters use the same payload. The
negative cases make a temporary digest-invalid copy for the corrupt-asset
case; no production or shared test helper is changed in this lane.

Generate the minimal qualification payload with `NetWasm.TimeZones generate`,
selecting both named zones above and writing the asset, Brotli payload, manifest,
and browser loader to a scratch directory. Point
`NETWASM_TIMEZONE_ASSET_PATH` at the resulting absolute `.nwtz` path. A
single-zone deployment asset is valid for an application but is deliberately
insufficient for this two-zone qualification corpus.
