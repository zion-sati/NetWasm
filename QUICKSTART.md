# NetWasm quickstart

NetWasm's normal developer experience uses packages from NuGet.org. It does not
require a NetWasm source checkout or a product-specific package source.

Install .NET SDK 10.0.300 or newer, Git, Python 3, and Emscripten SDK 6.0.7.
Activating emsdk supplies the supported Node.js 24+ and LLD 24+ toolchain:

```sh
git clone https://github.com/emscripten-core/emsdk.git ~/emsdk
cd ~/emsdk
./emsdk install 6.0.7
./emsdk activate 6.0.7
source ~/emsdk/emsdk_env.sh
```

Then create and run an application:

```sh
dotnet new install NetWasm.Templates@0.1.0-preview.64
mkdir HelloNetWasm
cd HelloNetWasm
dotnet new netwasm-app
dotnet restore
dotnet run
dotnet publish -c Release -o publish
```

The application uses an ordinary C# `Main` entry point. The generated launcher
and reusable `NetWasm.Hosting` modules own JavaScript execution details.

See the [complete SDK quickstart](docs/sdk-quickstart.md) for Windows setup,
capability grants, browser publishing, and library templates. Read the
[support status](docs/support-status.md) and [limitations](docs/LIMITATIONS.md)
before porting an existing application.

For contributors building the toolchain itself, install the same prerequisites
and run:

```sh
dotnet restore NetWasm.slnx
npm ci
eng/verify-toolchain.sh
dotnet build NetWasm.slnx -c Release --no-restore
```
