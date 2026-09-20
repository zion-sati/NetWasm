# NetWasm quickstart

Install .NET SDK 10.0.300 or newer. The application SDK restores its pinned
native build tools from NuGet for Linux x64/ARM64, macOS ARM64 and Windows
x64/ARM64. No Emscripten, Git or Python setup is needed to use it.

Create and run an application:

```sh
dotnet new install NetWasm.Templates
mkdir HelloNetWasm
cd HelloNetWasm
dotnet new netwasm-app
dotnet restore
dotnet run
dotnet publish -c Release -o publish
```

See the [complete SDK quickstart](docs/sdk-quickstart.md) for supported hosts,
capability grants, browser publishing, and library templates. Read the
[support status](docs/support-status.md) and [limitations](docs/LIMITATIONS.md)
before porting an existing application.

Contributors rebuilding the runtime packs need the pinned full Emscripten SDK;
see [contributor setup](CONTRIBUTING.md#local-setup).
