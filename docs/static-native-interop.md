# Static native C interop

NetWasm can link regular WebAssembly `.a` archives into the final
application. Managed code imports C symbols with ordinary .NET
`[LibraryImport]` declarations. Native code can call selected managed methods
through named `[UnmanagedCallersOnly]` symbols or supported address-taken
callbacks.

There is no NetWasm `LibImport` or `LibExport` attribute. There is also no
dynamic library search: the project maps a logical `LibraryImport` name to an
explicit archive for each Wasm target.

## Minimal two-way example

The following walkthrough was package-only qualified with the published
NetWasm SDK on .NET SDK 10.0.401. The sample archive was built with
Emscripten 6.0.7; `dotnet run -c Release` printed `42` and `41`.

Create an ordinary application:

```sh
dotnet new install NetWasm.Templates
dotnet new netwasm-app -n NativeSample
cd NativeSample
mkdir -p native/wasm32
```

Add `native/sample.c`:

```c
extern int managed_double(int value);

int native_add(int left, int right)
{
    return left + right;
}

int native_call_managed(int value)
{
    return managed_double(value) + 1;
}
```

Activate Emscripten, then create a regular archive:

```sh
source ~/emsdk/emsdk_env.sh
emcc -Oz -c native/sample.c -o native/wasm32/sample.o
emar rcs native/wasm32/libsample.a native/wasm32/sample.o
```

Applications consuming a prebuilt archive do not need Emscripten. NetWasm's
restored linker handles the final application link.

Enable the stock `LibraryImport` source generator and declare the provider in
the project:

```xml
<PropertyGroup>
  <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
</PropertyGroup>

<ItemGroup>
  <NativeLibrary Include="native/wasm32/libsample.a"
                 NetWasmLibraryName="sample"
                 WasmTarget="wasm32" />
</ItemGroup>
```

All three `NativeLibrary` fields are required. The archive path includes
`.a`; the logical name `sample` does not. It matches
`[LibraryImport("sample")]` ordinally.

Replace `Program.cs` with:

```csharp
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

internal static partial class Program
{
    [LibraryImport("sample", EntryPoint = "native_add")]
    private static partial int NativeAdd(int left, int right);

    [LibraryImport("sample", EntryPoint = "native_call_managed")]
    private static partial int NativeCallManaged(int value);

    [UnmanagedCallersOnly(EntryPoint = "managed_double",
        CallConvs = [typeof(CallConvCdecl)])]
    private static int ManagedDouble(int value) => value * 2;

    private static void Main()
    {
        Console.WriteLine(NativeAdd(20, 22));
        Console.WriteLine(NativeCallManaged(20));
    }
}
```

Run it:

```sh
dotnet run -c Release
```

```text
42
41
```

`managed_double` is a native linker symbol inside this final application. It
does not create a managed `.a` for unrelated consumers. The attributed method
must be static and must not be called directly from managed code. A named native
entry may call it only after normal managed application startup; native
constructors must not re-enter managed code.

## Supported profile

| Shape | Current boundary |
| --- | --- |
| Scalars | `int`/`uint`, `long`/`ulong`, `float`/`double`, `nint`/`nuint`, unmanaged pointers, and `void` results |
| By-reference values | Scalar `ref`/`out`; native code must not retain the address |
| Aggregates | Unmanaged sequential or explicit structs and unions by value, including nested, packed, fixed-buffer, padded, and qualified empty-root layouts |
| Arrays | Qualified counted input, output, and in-out arrays with explicit count/capacity and call-scoped caller-owned storage |
| Strings | Source-generated UTF-8 input marshalling, including null, empty, non-ASCII, and embedded NUL |
| Handles | `SafeHandle` inputs borrowed with call lifetime protection |
| Callbacks | Static scalar/pointer `UnmanagedCallersOnly` callbacks, named or address-taken, using the default C ABI or Cdecl and explicit registration lifetime |

The full layout, ownership, cache, and target-width rules remain in the
[CoreLib/runtime static-native profile](corelib-runtime.md#static-native-interop).

## Library and package propagation

A source library can declare `NativeLibrary` items; they propagate through
normal `ProjectReference` edges. A NuGet package can contribute package-
relative archive items from `buildTransitive` targets. The final application
selects the matching target width and links only providers reached by managed
imports. Unreached declarations do not retain an archive.

Regular object members and compatible LTO members are accepted when their width
and Wasm feature set match. Thin archives and direct consumer `.o` items are
rejected.

## Call host functions directly

`[WasmImportLinkage]` on a `[LibraryImport]` or `[DllImport]` declaration makes
the library name a WebAssembly import module and the entry point its function.
No archive defines it: the final module imports it, and the host supplies the
function. This is the fastest way to call JavaScript, or another WebAssembly
module, from C#:

```csharp
using System.Runtime.InteropServices;

public static partial class HostUi
{
    [LibraryImport("host_ui", EntryPoint = "create_node"), WasmImportLinkage]
    public static partial ulong CreateNode(uint type);
}
```

```js
const session = await open({
  modules: { host_ui: { create_node: type => BigInt(type) << 32n } },
});
```

The function receives and returns WebAssembly values: 32-bit integers and
floating-point values as `number`, 64-bit integers as `bigint`, and pointers
as addresses in the guest's memory. Anything the function throws reaches the
caller as a `JSException`. A module can also be bundled with a
`NetWasmJavaScriptModule` item, as for [JavaScript interop](javascript-interop.md).

A native archive can import host functions too, with the C attributes
`import_module` and `import_name`:

```c
__attribute__((import_module("host_ui"), import_name("create_node")))
extern uint64_t host_create_node(uint32_t type);
```

Declare each such module so the build keeps its imports:

```xml
<ItemGroup>
  <NetWasmImportModule Include="host_ui" />
</ItemGroup>
```

A module bundled with `NetWasmJavaScriptModule` is declared already. Without a
declaration, the build stops with NWPACK001 naming the import. C code cannot
unwind, so a function an archive calls must not throw; a throw leaves the
instance unusable.

Both need raw WebAssembly output: a `JsModule`, or an executable with
`NetWasmRawWasm=true`. A component imports host functions through
[WIT](wit-bindings.md) instead, and a component build with an import module
stops with NWSDK087. Module names that start with `netwasm` or `wasi` are
reserved.

## Deliberate exclusions

The static profile does not provide dynamic loading, runtime symbol lookup,
varargs, broad runtime/reflection marshalling, arbitrary custom marshallers,
`SetLastError`, `SuppressGCTransition`, native threads, direct C++ ABI, or C++
exceptions crossing managed frames.

`SafeHandle` `ref`, `out`, and return construction are unsupported.
Callback aggregates, arrays, byrefs, handles, callback-valued parameters or
results, arbitrary unmanaged `calli`, and delegate-to-native-pointer conversion
are also outside the profile. These exclusions are compile-time failures rather
than silent ABI guesses.
