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
| Scalars | `sbyte`/`byte`, `short`/`ushort`, `int`/`uint`, `long`/`ulong`, `float`/`double`, `nint`/`nuint`, unmanaged pointers, and `void` results |
| By-reference values | Scalar `ref`/`out`; native code must not retain the address |
| Aggregates | Unmanaged sequential or explicit structs and unions by value, including nested, packed, fixed-buffer, padded, and qualified empty-root layouts; with runtime marshalling, structs holding `bool` or `char` in the native layout described below |
| Arrays | Qualified counted input, output, and in-out arrays with explicit count/capacity and call-scoped caller-owned storage |
| Strings | Source-generated UTF-8 input marshalling, including null, empty, non-ASCII, and embedded NUL; with a raw `DllImport`, desktop .NET's runtime marshalling of `string` arguments and results (UTF-8 or UTF-16) |
| Handles | `SafeHandle` inputs borrowed with call lifetime protection; with `LibraryImport`, `SafeHandle` results, `out` and `ref` parameters, which .NET's `SafeHandleMarshaller` constructs from the handle type's public parameterless constructor; with a raw `DllImport`, desktop .NET's runtime marshalling of `SafeHandle` and `CriticalHandle` arguments, results, `out` and `ref` parameters, and `HandleRef` arguments |
| Raw `DllImport` arrays | Arrays of blittable elements, pinned for the call as desktop .NET pins them |
| Callbacks | Static `UnmanagedCallersOnly` callbacks, named or address-taken, with explicit registration lifetime, taking and returning scalars, pointers and structs the C ABI lays out by value, as imports do |
| Function pointers | Calls through `delegate* unmanaged`, to a native function or an `UnmanagedCallersOnly` method, with the shapes above |
| Calling conventions | Those .NET accepts on its 64-bit platforms, each the C ABI: the default, `Cdecl`, `Stdcall` and `Thiscall`, with `SuppressGCTransition` or `MemberFunction`, on imports, callbacks and function pointers |

The full layout, ownership, cache, and target-width rules remain in the
[CoreLib/runtime static-native profile](corelib-runtime.md#static-native-interop).

## Library and package propagation

A source library can declare `NativeLibrary` items; they propagate through
normal `ProjectReference` edges. A NuGet package can contribute package-
relative archive items from `buildTransitive` targets. The final application
selects the matching target width and links only providers reached by managed
imports. Unreached declarations do not retain an archive.

A declaration whose library no `NativeLibrary` item names, matched exactly by
`NetWasmLibraryName`, still compiles. Calling it throws `DllNotFoundException`,
as on .NET when a library cannot be loaded, and as Mono's WebAssembly build
handles a module it does not link. The build reports warning NW1017 for each
such declaration the program reaches. A package that P/Invokes a desktop
library on one platform path therefore builds, and fails only if that path
runs. To call the library, reference the package or `NativeLibrary` item that
provides it. A function missing from a linked library is still a link error.

Regular object members and compatible LTO members are accepted when their width
and Wasm feature set match. Thin archives and direct consumer `.o` items are
rejected.

## Archives built by Emscripten

Packages ship static archives built by Emscripten for .NET's WebAssembly SDKs
in one of two layouts, one archive per Emscripten version:

- SkiaSharp's and HarfBuzzSharp's:
  `build[Transitive]/<framework>/<library>.a/<version>/[<variant>/]<library>.a`.
- .NET's browser-wasm native assets, as SQLitePCLRaw's:
  `runtimes/browser-wasm/nativeassets/<framework>/<library>.a`, built with the
  Emscripten of that framework's SDK (`net8.0` 3.1.34, `net9.0` and `net10.0`
  3.1.56, `net11.0` 6.0.2).

An executable referencing such a package links its matching build with no
`NativeLibrary` item: the newest no newer than the Emscripten NetWasm's runtime
was built with (6.0.7), single-threaded, with SIMD where there is one. A
`NativeLibrary` item naming the same library (`NetWasmLibraryName`) replaces the
package's choice. The selection applies to wasm32; such packages ship no wasm64
builds.

What those archives need from Emscripten's toolchain is provided, and linked
only when an archive calls it:

| Need | Provided |
| --- | --- |
| Wasm exception handling | Archives in the exnref encoding link as they are. Archives in the legacy encoding (Emscripten 3 and 4) link too: the final module is translated to exnref |
| `setjmp`/`longjmp` (Wasm) | Both of Emscripten's ABIs (`saveSetjmp`/`testSetjmp` before 3.1.57, `__wasm_setjmp` after) and the `__c_longjmp` tag. A `longjmp` that escapes into managed code aborts, as on .NET for Linux |
| C++ standard library | libc++ and libc++abi; for an archive that throws or catches C++ exceptions, their builds with Wasm exception handling and libunwind, with the personality entry older Emscripten's landing pads call. A C++ exception that escapes into managed code aborts |
| The C library | Emscripten's, with the runtime answering clocks and `getentropy` from WASI 0.3. Files other than standard output and error, the time zone database and the network are unavailable, and fail as such |
| Emscripten's JavaScript functions | By output kind, below |

Emscripten implements some functions in JavaScript, such as WebGL's `gl*` and
`emscripten_has_asyncify`. NetWasm links stand-ins for them: each does
nothing and returns zero, so no GL context is ever created and Skia renders on
the CPU, as desktop .NET does without OpenGL. A native library that defines
one of them itself wins over the stand-in.

A `JsModule` or raw WebAssembly module whose JavaScript supplies these
functions declares each one, with the import module whose JavaScript supplies
it. Declaring any of the functions the stand-ins cover means the JavaScript
supplies them all, and the output links no stand-ins:

```xml
<ItemGroup>
  <NetWasmJavaScriptModule Include="webgl.mjs" Module="webgl" />
  <NetWasmEnvImport Include="glClear" Module="webgl" />
</ItemGroup>
```

The module imports `glClear` from `webgl` rather than from `env`, which stays
the runtime's own. A component build with `NetWasmEnvImport` stops with
NWSDK106, and one without `Module` with NWSDK107. A function that nothing
defines or declares fails the build, naming it.

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
declaration, the build stops with NWPACK001 naming the import. A declared
module that the final module does not import, such as one whose only caller
trimming removed, is neither bundled nor required. C code cannot
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
`SetLastError`, the `Fastcall` and `Swift` calling conventions, native threads,
direct C++ ABI, or C++ exceptions crossing managed frames.

`NativeLibrary` is there for libraries that probe for their native library, and
answers as .NET does for a library that is not there: `Load` throws
`DllNotFoundException`, `TryLoad` and `TryGetExport` return `false`, `GetExport`
throws `EntryPointNotFoundException`, `Free` does nothing and
`GetMainProgramHandle` throws `PlatformNotSupportedException`.
`SetDllImportResolver` accepts one resolver per assembly, as .NET does, but never
calls it: every `DllImport` binds to its linked archive when the program is
built.

A raw `DllImport` in an assembly that keeps runtime marshalling converts `bool`
and `char` as desktop .NET does on Unix, by value, by reference and as results:
`bool` as a 4-byte `BOOL`, or one byte with `[MarshalAs(UnmanagedType.U1)]` or
`I1`, with `true` as 1 and a native value true when it is not zero; `char` as a
UTF-16 code unit under `CharSet.Unicode` or `[MarshalAs(UnmanagedType.U2)]`, and
otherwise (`CharSet.Ansi`, `Auto` or none, or `U1`) as one ANSI byte, the first
byte of its UTF-8 encoding, with a byte past ASCII coming back as U+FFFD. A
`ref`, `in` or `out` value passes the address of a converted copy, which an
`out`-only parameter starts at zero. `VariantBool`, and a `[MarshalAs]` its
position cannot take, fail where reached, as desktop .NET fails them on Unix.
A `string` passes as desktop .NET passes it on Unix: as a NUL-terminated UTF-8
copy (`CharSet.Ansi`, `Auto` or none, `LPStr` or `LPUTF8Str`), freed when the
call returns, or under `CharSet.Unicode` or `LPWStr` pinned, as its own UTF-16
characters, which a NUL follows, with native writes showing in the string; a
null string passes a null pointer. Native code must not keep a string argument's
address. A `string` result is decoded from its NUL-terminated buffer (UTF-8, or
UTF-16 under `CharSet.Unicode` or `LPWStr`) and the buffer freed with
`Marshal.FreeCoTaskMem`, so a function returning memory from `malloc` works as
on desktop .NET; a null pointer is a null string.
A struct that holds a `bool` or `char`, at any depth, passes as desktop .NET's
runtime marshalling lays it out natively on Unix: each field at its C alignment
(capped by `Pack`, or at its `FieldOffset`), `bool` as a 4-byte `BOOL` or one
byte with `[MarshalAs(UnmanagedType.U1)]`, `char` as one ANSI byte or, when the
struct's `CharSet` is `Unicode` or the field says `U2`, its UTF-16 code unit, and
nested structs in their own native layout. The struct, its nested structs and
its enums may come from any assembly. By value the native copy is only
read, by reference it is copied back after the call (an `out`-only copy starts
at zero), and a struct result converts from the copy the function returns. A
struct with a field of a reference type, or a `[MarshalAs]` that sizes a fixed
array or string, fails where reached.
A struct that holds a `bool` or `char` also has this native layout for
`Marshal.SizeOf`, `OffsetOf`, `StructureToPtr`, `PtrToStructure` and
`DestroyStructure`, as on desktop .NET, whichever assembly calls them:
`StructureToPtr` zeroes the native block's padding and converts each field,
and `PtrToStructure` reads a nonzero `BOOL` as `true` and an ANSI byte as the
character it decodes to.

These members also convert what desktop .NET converts on Unix:

- A formatted class (`[StructLayout(LayoutKind.Sequential)]` or `Explicit`)
  lays out its base classes' fields first; `OffsetOf` finds its own fields and
  the non-private fields it inherits. `PtrToStructure` fills a new object or,
  with the `(IntPtr, object)` overloads, an existing one. `PtrToStructure<T>`
  runs the class's parameterless constructor; `PtrToStructure(IntPtr, Type)`
  creates the object without running it, and the native data then sets every
  marshalled field. A class without one throws `MissingMethodException`, and a
  class without layout throws `ArgumentException`.
- A `string` field is a pointer to a native copy freed with
  `Marshal.FreeCoTaskMem`, which `DestroyStructure` (and `StructureToPtr` with
  `fDeleteOld`) frees: UTF-8 for the default ANSI, `LPStr` and `LPUTF8Str`,
  UTF-16 for `LPWStr` and `CharSet.Unicode`; a null string is a null pointer.
  `ByValTStr` holds at most `SizeConst - 1` characters inline, then a
  terminator, and reads back as the text before it.
- `ByValArray` holds the first `SizeConst` elements of an array of primitives
  inline: booleans as 4-byte `BOOL`s or, with `ArraySubType = U1`, bytes, and
  characters as ANSI bytes unless the structure is Unicode. A shorter array
  throws `ArgumentException`, a null one writes zeros, and `PtrToStructure`
  reads a new array of `SizeConst` elements.
- A delegate field converts when it is null. A fixed buffer of `bool` or ANSI
  `char` converts its first element only, and one of UTF-16 `char`s copies
  whole, here and in a struct passed to a P/Invoke, as desktop .NET does.

A field desktop .NET rejects (an `object`, an array without `ByValArray`, a
`StringBuilder`, a class without layout) makes `SizeOf` and `OffsetOf` throw
`ArgumentException` and the converting members `TypeLoadException`. An embedded
formatted class, a non-null delegate and an array of structures throw
`PlatformNotSupportedException`.
A call through an unmanaged function pointer (`delegate* unmanaged`) in such an
assembly marshals as desktop .NET's runtime marshalling does at an unmanaged
`calli`, which is as a raw `DllImport` without attributes: `bool` as a `BOOL`,
`char` and `string` as ANSI (UTF-8), and a struct that holds a `bool` or `char`
as its native copy, by value and as a result. A function pointer's signature
carries no `[MarshalAs]`, `[In]` or `[Out]`.
It rejects the rest of what desktop .NET's runtime marshalling would convert:
strings by reference, `StringBuilder`, delegates, other classes, and arrays
whose elements need conversion.
Callback arrays, byrefs, handles, callback-valued parameters or
results, function pointers with another calling convention, and
delegate-to-native-pointer conversion are also outside the profile. These exclusions are compile-time failures rather
than silent ABI guesses.

As on CoreCLR, a NUL follows every string's characters, so a string pinned with
`fixed (char* p = text)`, or by `LibraryImport`'s UTF-16 marshalling, reaches
native code terminated. `NativeMemory` and `Marshal`'s `AllocHGlobal`,
`AllocCoTaskMem`, their `ReAlloc` and `Free` forms are the C library's `malloc`,
`realloc` and `free`, as on .NET for Unix: a block native code allocates can be
freed from managed code, and one managed code allocates can be freed by native
code.
