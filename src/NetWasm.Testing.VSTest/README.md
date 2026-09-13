# NetWasm.Testing.VSTest

`NetWasm.Testing.VSTest` is the framework-neutral bridge between stock VSTest
and a test project targeting `netwasm0.1`. Its standard VSTest runtime-provider
extension recognizes only `NetWasm,Version=v0.1` and starts a pinned portable
desktop testhost under the `dotnet` host supplied by the invoking test run.

The package appends its extension directory to `VSTestTestAdapterPath`. It does
not define or replace the `VSTest` target, inject `Microsoft.NET.Test.Sdk`, add
a desktop target to the guest project, discover host tools, or contain a test
framework protocol. Framework packages supply ordinary VSTest adapters and use
the `INetWasmArtifactClient` from the packaged Hosting client to execute the
SDK-emitted artifact descriptor.

The portable testhost closure is pinned to Microsoft TestPlatform 18.0.1. Its
package-relative manifest records and validates the exact portable files before
launch. No RID-specific host executable or native test platform asset is
packaged.

The adapter contract and ordinary `dotnet test` workflow are documented in the
repository's SDK and testing documentation.
