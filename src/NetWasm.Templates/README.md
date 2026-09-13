# NetWasm.Templates

This package contains the local-qualification `dotnet new` templates for a
NetWasm application and a dual-target library. The templates use the ordinary
`NetWasm.Sdk` project SDK and the `netwasm0.1` profile.

Application templates do not copy reusable JavaScript host files. The SDK
generates the application-specific descriptors and bootstrap and stages the
selected `NetWasm.Hosting` modules during build and publish.

The package is not yet a public release or a compatibility promise. The
production consumer contract installs it from NuGet.org and uses activated
Emscripten 6.0.7, which supplies Node.js 24+ and LLD 24+; owner-approved local
feeds are qualification infrastructure only. See the repository's
[package-consumer quickstart](../../docs/sdk-quickstart.md).
