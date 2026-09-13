# NetWasm.Hosting.Build

Private MSBuild composition used by `NetWasm.Sdk` to resolve documented host
prerequisites, consume the platform-neutral `NetWasm.Toolchain` package, and
produce manifest-bound local or browser Hosting bundles.

Application projects should reference `NetWasm.Sdk`; this package is restored
transitively by the SDK and does not define a public application API.
