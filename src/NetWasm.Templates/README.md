# NetWasm.Templates

This package contains the `dotnet new` templates for a
NetWasm application and a dual-target library. The templates use the
`NetWasm.Sdk` project SDK and the `netwasm0.1` profile.

Application templates do not copy reusable JavaScript host files. The SDK
generates the application-specific descriptors and bootstrap and stages the
selected `NetWasm.Hosting` modules during build and publish.

The package follows NetWasm's documented support profile. The application
template restores the native host tools it needs through NuGet; the library
template does not select a host package. See the repository's
[package-consumer quickstart](../../docs/sdk-quickstart.md).
