# Generate C# bindings from WIT

NetWasm converts a selected WIT world into deterministic, reflection-free C#
source. The SDK generates it during the build from `NetWasmWit` items, and the
standalone `NetWasm.Wit.Bindings.Tool` produces the same source for other
workflows.

## Generate bindings during the build

Add a `NetWasmWit` item for each WIT input:

```xml
<ItemGroup>
  <NetWasmWit Include="wit/service.wit"
              World="docs:bindings@1.0.0/service"
              Namespace="MyApp.Host"
              Accessibility="Internal" />
</ItemGroup>
```

`Include` is a WIT file or a WIT package directory with its `deps`. `World`
selects a world and may be omitted when the input contains exactly one.
`Namespace` replaces the namespace the WIT package name gives, and
`Accessibility` is `Public` (the default) or `Internal`. Each item's bindings
compile with the project and are generated again when its WIT changes; two
items need distinct file or directory names.

The build runs the generator on the restored host tools, so it needs no Node.js
installation, in an application or a class library. With
`RestorePackagesWithLockFile`, also set `NetWasmRequiresHostTools` to `true` so
each development host keeps its own lock file (NWSDK089).

Bindings declare the managed side of a world. The component itself is still
built against the world that `NetWasmWitPath` and `NetWasmWorld` select; see
[Use the same world at runtime](#use-the-same-world-at-runtime).

## Run the standalone tool

Install the released tool:

```sh
dotnet tool install --global NetWasm.Wit.Bindings.Tool
```

The package includes NetWasm's pinned platform-neutral `wasm-tools` module.
It requires Node.js 24 or newer on `PATH`, through `NETWASM_NODE_PATH`, or
through `EMSDK_NODE`. It does not require a native `wasm-tools` executable or
Emscripten.

For this `service.wit`:

```wit
package docs:bindings@1.0.0;

interface host {
  record point {
    x: s32,
    y: s32,
  }

  variant outcome {
    value(s32),
    message(string),
  }

  transform: func(input: point) -> outcome;
}

world service {
  import host;
  export evaluate: func(value: s32) -> s32;
}
```

run:

```sh
netwasm-wit-bindgen \
  --wit service.wit \
  --world docs:bindings@1.0.0/service \
  --output Bindings.g.cs \
  --accessibility internal
```

Omit `--world` when the input contains exactly one world. `--wit` accepts a
WIT file or package directory, or `-` to read the WIT from standard input. A
shell's process substitution works as well, as in
`--wit <(generate-wit)`. `--output -` writes the source to standard output.
`--accessibility` is optional and accepts `public` (the default) or
`internal`; it changes the accessibility of generated top-level declarations.
`--namespace` is optional and sets the namespace of the generated source. By
default the namespace comes from the WIT package name, as below.

The example above, directory input, omitted single-world selection, both
accessibility modes, and absolute input/output paths were executed against the
published tool. Its generated source contains
`HostImports.Transform(Point)` and this partial export declaration:

```csharp
namespace NetWasm.Wit.Docs.Bindings._1._0._0;

internal static partial class ServiceExports
{
    public static partial int Evaluate(int value);
}
```

Implement the partial method in application source:

```csharp
namespace NetWasm.Wit.Docs.Bindings._1._0._0;

internal static partial class ServiceExports
{
    public static partial int Evaluate(int value) => value * 2;
}
```

Regenerate `Bindings.g.cs` whenever the selected WIT world changes, or let the
build generate it from a `NetWasmWit` item instead.

## Run the generated bindings in a browser worker

To exercise both directions in the example above, create an application named
`BindingConsumer` with the template, put the initial contract in
`wit/service.wit`, and generate `Bindings.g.cs` before adding the worker
world:

```sh
dotnet new netwasm-app -n BindingConsumer
cd BindingConsumer
netwasm-wit-bindgen --wit wit/service.wit \
  --world docs:bindings@1.0.0/service \
  --output Bindings.g.cs --accessibility internal
```

Append this world to `wit/service.wit`:

```wit
world worker {
  include service;
  include netwasm:platform/async-platform@1.0.0;
}
```

The worker SDK materializes the platform dependency during the build. Use a WIT
package **directory** in `NetWasmWitPath`, separate from `obj` and `bin`.
For this example, replace the application project with:

```xml
<Project Sdk="NetWasm.Sdk">
  <PropertyGroup>
    <OutputType>WitWebWorker</OutputType>
    <TargetFramework>netwasm0.1</TargetFramework>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <NetWasmWitPath>$(MSBuildProjectDirectory)/wit</NetWasmWitPath>
    <NetWasmWorld>docs:bindings@1.0.0/worker</NetWasmWorld>
    <NetWasmApplicationWorld>docs:bindings@1.0.0/service</NetWasmApplicationWorld>
  </PropertyGroup>
  <ItemGroup>
    <NetWasmApplicationImport Include="host.mjs"
                              Module="docs:bindings/host@1.0.0"
                              ArtifactPath="host.mjs" />
    <Content Include="index.html;app.mjs"
             CopyToPublishDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

Replace `Program.cs` with the implementation that calls the generated import:

```csharp
namespace NetWasm.Wit.Docs.Bindings._1._0._0;

internal static partial class ServiceExports
{
    public static partial int Evaluate(int value) =>
        HostImports.Transform(new Point(value, 0)).ValueValue;
}
```

Implement the WIT host import in `host.mjs`:

```js
export function transform(point) {
  return { tag: "value", val: point.x * 2 + point.y };
}
```

Add an `index.html` with `<output id="result">Starting...</output>` and
`<script type="module" src="./app.mjs"></script>`. In `app.mjs`:

```js
import { createWorker } from "./browser/BindingConsumer.worker-client.mjs";

const worker = await createWorker();
try {
  document.querySelector("#result").textContent =
    String(await worker.evaluate(21));
} finally {
  await worker.dispose();
}
```

Run `dotnet publish -c Release`, serve
`bin/Release/netwasm0.1/publish` over HTTP, and open `index.html`. It displays
`42`. This example exercises the generated record and variant marshalling,
JavaScript host import, managed export, and worker client using the released
packages.

After the worker world has been added, regenerate from the SDK's materialized
package, which contains the platform dependency. For the default Release wasm32
layout:

```sh
dotnet build -c Release
netwasm-wit-bindgen \
  --wit obj/Release/netwasm0.1/netwasm/wasm32/wit-worker-source \
  --world docs:bindings@1.0.0/service \
  --output Bindings.g.cs --accessibility internal
dotnet publish -c Release
```

The `obj` path is an intermediate build product. Keep authored WIT under
`wit` and regenerate after restoring/building with the selected SDK.

## Path and dependency rules

The packaged `wasm-tools` process is sandboxed. Its current working directory
is available as `.`, and absolute input paths are mapped explicitly. Run from
the WIT package/project directory or use absolute paths. A relative path that
escapes the working directory, such as `../contracts/service.wit`, is not a
portable invocation and can fail with an insufficient-capabilities error.

The tool does not download imported WIT packages. Keep every referenced package
in the input package's standard WIT dependency layout before generation.
Selecting a world that includes a missing package fails with `NW1009` and
names the unresolved package.

## Generated model

| WIT shape | Generated C# shape |
| --- | --- |
| `bool`, signed/unsigned integers, `f32`, `f64` | Corresponding C# scalar |
| `char` | `uint` Unicode scalar value |
| `string` | `string` |
| `list<T>` | `T[]` |
| `option<T>` | `WitOption<T>` |
| `tuple` | `ValueTuple` or C# tuple |
| `result<T, E>` | `WitResult<T, E>`; absent arms use `WitUnit` |
| record | Immutable generated struct |
| variant | Tag enum plus generated value struct |
| enum and flags | Generated enum; wide flags use the generated multiword representation |
| resource, `own`, and `borrow` | Generated resource handle classes with explicit ownership/disposal |
| imports | Static wrapper methods, in partial classes, that lower managed values to the canonical ABI |
| a world's own functions, types and resources | Members of `<World>Imports` and `<World>Exports`, bound with the one-argument `[WitImport("name")]` and `[WitExport("name")]` |
| exports | Partial managed methods plus generated canonical ABI entry points and post-return cleanup |

The generated marshalling is explicit C# and remains visible to closed-world
reachability. It does not require reflection or a runtime metadata registry.

The bindings, and the methods that implement a world's exports, can live in a
library the application references. The selected world binds each of its
exports to exactly one `[WitExport]` method, and its post-return cleanup to at
most one `[WitPostReturn]` method, in any assembly of the application. When the
application selects no world, a library's WIT exports are unused.

Asynchronous WIT functions and `future`/`stream` types are currently
unsupported by the standalone binding generator. WIT Web Worker application
exports additionally reject resource-bearing contracts even though the general
binding generator supports resources. These are explicit generation failures,
not silently reduced contracts.

## Use the same world at runtime

Generation defines managed declarations; it does not select the application's
runtime boundary. The project must compile and package against the matching WIT
world. For a browser worker, set `NetWasmWitPath`, `NetWasmWorld`, and the
application-only `NetWasmApplicationWorld` as described in
[C# Web Workers](web-workers.md#wit-workers).

Do not mix up the three interop planes:

- WIT imports/exports are language-neutral Component Model contracts.
- `[JSImport]`/`[JSExport]` use NetWasm's direct JavaScript ABI.
- `[LibraryImport]` and `[UnmanagedCallersOnly]` link a static C ABI inside
  the final application.

Choose the narrowest boundary that matches the host and data model.
