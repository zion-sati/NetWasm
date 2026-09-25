# Exact-generated-assembly qualification

This maintainer gate builds each generator fixture on desktop .NET, asserts its
observations, then compiles those exact assembly bytes with the current source
compiler for wasm32 and wasm64. Both Debug and Release CIL are tested. It does
not claim linked collector qualification or package-only compiler qualification.

Build the repository's CompilerHost, OracleHost and CoreLib in Release first.
Use the SDK pinned by the repository. Supply an immutable restored NuGet package
root containing the selected NetWasm library packages at **0.4.1**, and the
matching Libraries source snapshot. The source is needed only for the desktop
DI/binder contract assembly; generation remains ordinary MSBuild analyzer work.
Record the source commit and verify package provenance before accepting a run.

```sh
node tests/end-to-end/generated-assembly/run.mjs \
  /absolute/new-output /absolute/restored-package-root /absolute/libraries-source
```

An optional final argument selects `stj`, `regex`, `logging`, `activation`, or
`binding` for focused work. Omitting it runs all five. The Extended harness uses
`NETWASM_GENERATOR_PACKAGE_ROOT` and `NETWASM_LIBRARIES_SOURCE_ROOT`; missing
prerequisites fail rather than skip this gate. No private checkout path is built
into the runner. Fast executes only the in-memory observation contracts.

The output directory must not exist. Desktop restores, generated files,
compiler requests, modules, logs and receipts stay under that directory. Do not
move or remove its package cache while evaluating the receipt. Any failure
leaves the gate RED; there is no known-failure allowlist. The combined activation
fixture intentionally retains desktop keyed-service calls. Binding has an
independent fixture so an activation rejection does not mask binding execution.

The desktop DI/binder contract assembly compiles frozen port source against
desktop framework references. Its co-located internal section-value interface
does not prove explicit-null-provider discrimination. Those limitations remain
separate from the package-only profile tests.
