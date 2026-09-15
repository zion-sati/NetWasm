# NetWasm quickstart

Install .NET SDK 10.0.300 or newer, Git, Python 3, and Emscripten SDK 6.0.7.
Activating emsdk supplies the supported Node.js 24+ and LLD 24+ toolchain:

```sh
git clone https://github.com/emscripten-core/emsdk.git ~/emsdk
cd ~/emsdk
./emsdk install 6.0.7
./emsdk activate 6.0.7
source ~/emsdk/emsdk_env.sh
```

Create and run an application:

```sh
dotnet new install "NetWasm.Templates@*-*"
mkdir HelloNetWasm
cd HelloNetWasm
dotnet new netwasm-app
dotnet restore
dotnet run
dotnet publish -c Release -o publish
```

`@*-*` selects the latest templates, including prereleases. Omit it for stable
releases only. The generated project pins a concrete SDK version.

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
