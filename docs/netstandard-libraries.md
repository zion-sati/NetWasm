# .NET Standard libraries

A `netwasm0.1` project can reference NuGet packages built for .NET Standard. This page says which packages restore,
what happens when a library reaches something NetWasm does not provide, and how a library author can avoid those
paths. The [support status](support-status.md) page remains the inventory of what NetWasm implements.

## Which packages restore

NuGet picks a package's best asset for `netwasm0.1`: a `netwasm0.1` asset if the package has one, otherwise its
.NET Standard 2.1, 2.0 or earlier asset. A package that ships only `net8.0` or later assets has nothing NetWasm can
use; its author can add `netwasm0.1` (or `netstandard2.1`) to the package's target frameworks. Adding `netwasm0.1`
keeps .NET 8 and later APIs, such as hardware intrinsics, that .NET Standard does not have.

Libraries built against .NET's reference assemblies (System.Runtime, System.Buffers and the rest) bind to NetWasm's
framework without changes, and packages that only backport framework assemblies are not restored.

## When a library reaches something NetWasm does not provide

NetWasm compiles the whole program ahead of time. Most unsupported operations still compile and throw a
`PlatformNotSupportedException` that names the operation when they run, so code that catches it, or never reaches
it, keeps working. The families a library meets most often:

- **Reflection.** Looking up members at run time (`Type.GetConstructors`, `GetMethod`, `GetProperties`, `GetFields`),
  `GetTypeInfo()` discovery, a type's `Assembly` and attribute flags, custom attribute queries whose target is found
  at run time, and `Type.GetType(string)`. Attribute queries the compiler can bound, and type names read from a
  `Type` value, are supported; see the [support status](support-status.md). Newtonsoft.Json is the common case: it
  builds, but it finds an object's properties and constructors at run time, and its `JsonWriter` reads
  `JsonToken`'s fields the same way when first used, so serializing objects and writing any JSON throw. Use
  System.Text.Json with source generation instead.
- **Manifest resources.** NetWasm deploys no embedded resources. `Assembly.GetManifestResourceStream` throws, and
  the NetWasm SDK sets `UseSystemResourceKeys` to true, as Blazor WebAssembly's SDK does. A package that formats its
  messages through generated `SR` resources (all of Microsoft.Extensions and many others) therefore reports the
  resource key, such as `TryAddIndistinguishableTypeToEnumerable`, instead of the English sentence, and its
  exceptions keep their types. Where a package embeds trimming substitutions for the switch, as Microsoft.Extensions
  packages do, its resource lookup leaves the program altogether.
- **File change notifications.** WASI 0.3 has no way to watch the file system, so `FileSystemWatcher` throws when it
  starts raising events. Microsoft.Extensions.Hosting watches `appsettings.json` by default; turn that off with the
  host setting `hostBuilder:reloadConfigOnChange=false`, for example
  `Host.CreateApplicationBuilder(["--hostBuilder:reloadConfigOnChange=false"])`.

Native interop follows the same rule. A `[DllImport]` or `[LibraryImport]` of a library the build does not link
compiles, throws `DllNotFoundException` when called, and the build warns with NW1017 for each one the program
reaches. One of a linked library must use the [supported native ABI](static-native-interop.md), or the build fails
with NW1012 or NWPACK001, naming the import and its library.

## Guidance for library authors

Each of these keeps a library working on NetWasm and costs nothing elsewhere.

- **Guard platform-specific code with a check NetWasm folds.** `OperatingSystem.IsWindows()`, and
  `RuntimeInformation.IsOSPlatform(OSPlatform.Windows)` (likewise Linux, OSX and FreeBSD) are false on NetWasm at build
  time, so code inside `if (OperatingSystem.IsWindows())`, and the Windows P/Invokes it calls, is left out of the
  program. Write the check in the condition itself: a result stored in a field and read later is a run-time value,
  and the guarded P/Invoke is then reached.
- **Probe behaviour, not the runtime's identity.** A check such as `Type.GetType("Mono.Runtime") != null` needs type
  names. Test the behaviour the check stands for instead. For example, a library that works around Mono parsing
  `"/path"` as an absolute URI can test `new Uri("/x", UriKind.RelativeOrAbsolute).IsAbsoluteUri`. When the runtime
  really matters, `RuntimeInformation.FrameworkDescription` names it (NetWasm reports `NetWasm <version>`); .NET 5
  and later targets also have `OperatingSystem.IsBrowser()`, and .NET 8 and later `OperatingSystem.IsWasi()`.
- **Reference optional companions instead of finding them by name.** Rather than `Type.GetType("Vendor.Init, Vendor")`
  and invoking a method on the result, take a direct reference, ask the application to call the companion's
  initializer, or, on .NET 5 and later targets, register from a `[ModuleInitializer]` in the companion.
- **Activate services without constructor discovery.** Microsoft.Extensions.DependencyInjection finds constructors by
  reflection for type registrations such as `AddSingleton<IService, Service>()`. A registration through a factory,
  `AddSingleton<IService>(provider => new Service(provider.GetRequiredService<IOther>()))`, needs no discovery.
  Applications can use `NetWasm.Microsoft.Extensions.DependencyInjection`, which generates activation at build time.
- **Generate what reflection would read.** Serialize with System.Text.Json source generation (`JsonSerializerContext`)
  rather than reflection-based serializers, and take version strings or other build facts from a constant the build
  generates rather than from `typeof(T).Assembly` and its attributes.
- **Keep message resources optional.** Exception types and properties should carry the information callers need;
  NetWasm shows resource keys, not sentences.
