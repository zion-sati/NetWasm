# Static native C interop

NetWasm 0.6.0 can link regular WebAssembly `.a` archives into the final
application. Managed code imports C symbols with ordinary .NET
`[LibraryImport]` declarations. Native code can call selected managed methods
through named `[UnmanagedCallersOnly]` symbols or supported address-taken
callbacks.

There is no NetWasm `LibImport` or `LibExport` attribute. There is also no
dynamic library search: the project maps a logical `LibraryImport` name to an
explicit archive for each Wasm target.

## Minimal two-way example

The following walkthrough was package-only qualified with the published
NetWasm 0.6.0 SDK on .NET SDK 10.0.401. The sample archive was built with
Emscripten 6.0.7; `dotnet run -c Release` printed `42` and `41`.

Create an ordinary application:

```sh
dotnet new install NetWasm.Templates@0.6.0
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

