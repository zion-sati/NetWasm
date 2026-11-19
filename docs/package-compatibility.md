# Popular NuGet packages

A `net10.0-netwasm` project restores NuGet packages as a desktop .NET 10
project does, taking each package's build for .NET 10 or earlier, or for .NET
Standard, as [.NET Standard libraries](netstandard-libraries.md) explains. To
show how the packages people use most behave, 100 of the most-downloaded
nuget.org packages each ran the quick start from its README, built for NetWasm
and for desktop .NET 11.

**56 of the 100 run unchanged**, printing exactly what desktop .NET prints.
Most of the other 44 stop where the library uses run-time reflection, which
NetWasm leaves out so that programs stay small. For several of them, a
source-generated package or a NetWasm build of the same library does the job;
see [Replacements](#replacements).

## How the packages were tested

- **Which packages.** The 100 most-downloaded packages on nuget.org, at their
  latest stable versions on 2026-11-12, that ship a .NET Standard 2.x build.
  Framework packages (`System.*`, `Microsoft.Bcl.*` and the like), packages
  without libraries (analyzers and build tools) and test frameworks are left
  out. Where a package also ships a build for a newer .NET, the
  `net10.0-netwasm` program uses that one.
- **What ran.** One program per package: its README quick start, or a short
  program that builds and exercises the package where the quick start needs a
  service such as a database or a cloud account. The same source builds for
  desktop .NET 11 and for NetWasm.
- **What counts.** A package runs unchanged when its NetWasm Release build
  succeeds and the program's exit status and output equal desktop .NET's.

Tested as `net10.0-netwasm` programs against desktop .NET 11.

## Results

| # | Package | Version | Runs unchanged | First blocker | Works instead |
| ---: | --- | --- | :---: | --- | --- |
| 1 | [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/13.0.4) | 13.0.4 | No | Reflection: looks members up at run time | System.Text.Json with source generation (`JsonSerializerContext`), in the box |
| 2 | [Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/Microsoft.Extensions.DependencyInjection/11.0.0) | 11.0.0 | No | Reflection: looks members up at run time (`Type.GetConstructors`) | [NetWasm.Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/NetWasm.Microsoft.Extensions.DependencyInjection), the same code |
| 3 | [Microsoft.Extensions.Logging](https://www.nuget.org/packages/Microsoft.Extensions.Logging/11.0.0) | 11.0.0 | No | Reflection: looks members up at run time (`Type.GetConstructors`) | [NetWasm.Microsoft.Extensions.Logging](https://www.nuget.org/packages/NetWasm.Microsoft.Extensions.Logging), the same code |
| 4 | [Azure.Core](https://www.nuget.org/packages/Azure.Core/1.65.0) | 1.65.0 | Yes |  |  |
| 5 | [Serilog](https://www.nuget.org/packages/Serilog/4.4.0) | 4.4.0 | No | Reflection: destructuring an object (`{@Value}`) reads its properties at run time; Serilog writes an error in place of the value |  |
| 6 | [Microsoft.IdentityModel.Abstractions](https://www.nuget.org/packages/Microsoft.IdentityModel.Abstractions/8.23.0) | 8.23.0 | Yes |  |  |
| 7 | [Microsoft.Identity.Client](https://www.nuget.org/packages/Microsoft.Identity.Client/4.92.0) | 4.92.0 | Yes |  |  |
| 8 | [Microsoft.Extensions.Http](https://www.nuget.org/packages/Microsoft.Extensions.Http/11.0.0) | 11.0.0 | No | Reflection: looks members up at run time (`Type.GetConstructors`) | [NetWasm.Microsoft.Extensions.Http](https://www.nuget.org/packages/NetWasm.Microsoft.Extensions.Http), the same code |
| 9 | [Microsoft.Win32.SystemEvents](https://www.nuget.org/packages/Microsoft.Win32.SystemEvents/11.0.0) | 11.0.0 | Yes |  |  |
| 10 | [Microsoft.Extensions.Diagnostics.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.Abstractions/11.0.0) | 11.0.0 | Yes |  |  |
| 11 | [Azure.Identity](https://www.nuget.org/packages/Azure.Identity/1.21.0) | 1.21.0 | No | Reflection: looks members up at run time |  |
| 12 | [AWSSDK.Core](https://www.nuget.org/packages/AWSSDK.Core/4.0.103) | 4.0.103 | Yes |  |  |
| 13 | [Microsoft.Identity.Client.Extensions.Msal](https://www.nuget.org/packages/Microsoft.Identity.Client.Extensions.Msal/4.92.0) | 4.92.0 | Yes |  |  |
| 14 | [Castle.Core](https://www.nuget.org/packages/Castle.Core/5.2.1) | 5.2.1 | No | Reflection: looks members up at run time (`Type.GetConstructor`) |  |
| 15 | [Microsoft.OpenApi](https://www.nuget.org/packages/Microsoft.OpenApi/3.11.0) | 3.11.0 | Yes |  |  |
| 16 | [Polly](https://www.nuget.org/packages/Polly/8.9.1) | 8.9.1 | No | Reflection: validates its options' attributes (`Validator`) |  |
| 17 | [Microsoft.Data.SqlClient](https://www.nuget.org/packages/Microsoft.Data.SqlClient/7.1.1) | 7.1.1 | No | Its .NET Standard build supports only Windows |  |
| 18 | [Microsoft.Extensions.Hosting](https://www.nuget.org/packages/Microsoft.Extensions.Hosting/11.0.0) | 11.0.0 | No | Reflection: looks members up at run time (`Type.GetConstructors`) |  |
| 19 | [Google.Protobuf](https://www.nuget.org/packages/Google.Protobuf/3.36.2) | 3.36.2 | No | Reflection: looks members up at run time |  |
| 20 | [Newtonsoft.Json.Bson](https://www.nuget.org/packages/Newtonsoft.Json.Bson/1.0.3) | 1.0.3 | No | Reflection: looks members up at run time |  |
| 21 | [Microsoft.Extensions.Diagnostics](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics/11.0.0) | 11.0.0 | No | Reflection: looks members up at run time (`Type.GetConstructors`) |  |
| 22 | [Microsoft.ApplicationInsights](https://www.nuget.org/packages/Microsoft.ApplicationInsights/3.2.0) | 3.2.0 | Yes |  |  |
| 23 | [Microsoft.Extensions.Logging.EventLog](https://www.nuget.org/packages/Microsoft.Extensions.Logging.EventLog/11.0.0) | 11.0.0 | Yes |  |  |
| 24 | [StackExchange.Redis](https://www.nuget.org/packages/StackExchange.Redis/3.4.31) | 3.4.31 | No | Reflection: looks members up at run time |  |
| 25 | [Serilog.Sinks.File](https://www.nuget.org/packages/Serilog.Sinks.File/7.0.0) | 7.0.0 | Yes |  |  |
| 26 | [Serilog.Sinks.Console](https://www.nuget.org/packages/Serilog.Sinks.Console/6.1.1) | 6.1.1 | Yes |  |  |
| 27 | [Microsoft.Extensions.Diagnostics.HealthChecks](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.HealthChecks/11.0.0) | 11.0.0 | No | Reflection: looks members up at run time (`Type.GetConstructors`) |  |
| 28 | [Grpc.Core.Api](https://www.nuget.org/packages/Grpc.Core.Api/2.85.0) | 2.85.0 | Yes |  |  |
| 29 | [Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions](https://www.nuget.org/packages/Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions/11.0.0) | 11.0.0 | Yes |  |  |
| 30 | [Humanizer.Core](https://www.nuget.org/packages/Humanizer.Core/3.0.10) | 3.0.10 | Yes |  |  |
| 31 | [Serilog.Extensions.Logging](https://www.nuget.org/packages/Serilog.Extensions.Logging/10.0.0) | 10.0.0 | No | Reflection: looks members up at run time (`Type.GetConstructors`) |  |
| 32 | [AutoMapper](https://www.nuget.org/packages/AutoMapper/16.3.0) | 16.3.0 | No | Reflection: looks members up at run time | [Mapperly](https://www.nuget.org/packages/Riok.Mapperly), whose mappers are source-generated |
| 33 | [Serilog.Settings.Configuration](https://www.nuget.org/packages/Serilog.Settings.Configuration/10.0.1) | 10.0.1 | Yes |  |  |
| 34 | [Serilog.Formatting.Compact](https://www.nuget.org/packages/Serilog.Formatting.Compact/3.0.0) | 3.0.0 | Yes |  |  |
| 35 | [Grpc.Net.Common](https://www.nuget.org/packages/Grpc.Net.Common/2.85.0) | 2.85.0 | Yes |  |  |
| 36 | [Pipelines.Sockets.Unofficial](https://www.nuget.org/packages/Pipelines.Sockets.Unofficial/2.2.16) | 2.2.16 | Yes |  |  |
| 37 | [Grpc.Net.Client](https://www.nuget.org/packages/Grpc.Net.Client/2.85.0) | 2.85.0 | Yes |  |  |
| 38 | [OpenTelemetry.Api](https://www.nuget.org/packages/OpenTelemetry.Api/1.21.0) | 1.21.0 | Yes |  |  |
| 39 | [Serilog.Extensions.Hosting](https://www.nuget.org/packages/Serilog.Extensions.Hosting/10.0.0) | 10.0.0 | No | Reflection: looks members up at run time (`Type.GetConstructors`) |  |
| 40 | [Azure.Storage.Common](https://www.nuget.org/packages/Azure.Storage.Common/12.29.0) | 12.29.0 | Yes |  |  |
| 41 | [Azure.Storage.Blobs](https://www.nuget.org/packages/Azure.Storage.Blobs/12.30.1) | 12.30.1 | No | Reflection: looks members up at run time (`Type.GetMethod`) |  |
| 42 | [Serilog.AspNetCore](https://www.nuget.org/packages/Serilog.AspNetCore/10.0.0) | 10.0.0 | No | Its build for current .NET needs ASP.NET Core's shared framework, which NetWasm does not provide |  |
| 43 | [Serilog.Sinks.Debug](https://www.nuget.org/packages/Serilog.Sinks.Debug/3.0.0) | 3.0.0 | Yes |  |  |
| 44 | [Dapper](https://www.nuget.org/packages/Dapper/2.1.89) | 2.1.89 | No | Needs SQLite's native library, and finds its provider by reflection |  |
| 45 | [OpenTelemetry](https://www.nuget.org/packages/OpenTelemetry/1.21.0) | 1.21.0 | No | Reflection: queries custom attributes at run time |  |
| 46 | [Azure.Security.KeyVault.Secrets](https://www.nuget.org/packages/Azure.Security.KeyVault.Secrets/4.12.0) | 4.12.0 | Yes |  |  |
| 47 | [Microsoft.Azure.Amqp](https://www.nuget.org/packages/Microsoft.Azure.Amqp/2.7.3) | 2.7.3 | Yes |  |  |
| 48 | [Polly.Core](https://www.nuget.org/packages/Polly.Core/8.9.1) | 8.9.1 | No | Reflection: validates its options' attributes (`Validator`) |  |
| 49 | [OpenTelemetry.Api.ProviderBuilderExtensions](https://www.nuget.org/packages/OpenTelemetry.Api.ProviderBuilderExtensions/1.21.0) | 1.21.0 | Yes |  |  |
| 50 | [CsvHelper](https://www.nuget.org/packages/CsvHelper/34.0.0) | 34.0.0 | Yes |  |  |
| 51 | [Microsoft.SqlServer.Server](https://www.nuget.org/packages/Microsoft.SqlServer.Server/1.0.0) | 1.0.0 | Yes |  |  |
| 52 | [AWSSDK.S3](https://www.nuget.org/packages/AWSSDK.S3/4.0.104.12) | 4.0.104.12 | No | Reflection: looks members up at run time |  |
| 53 | [NLog](https://www.nuget.org/packages/NLog/6.2.2) | 6.2.2 | No | Reflection: looks members up at run time (`Type.GetProperty`) |  |
| 54 | [Google.Apis.Auth](https://www.nuget.org/packages/Google.Apis.Auth/1.78.0) | 1.78.0 | Yes |  |  |
| 55 | [Microsoft.Azure.WebJobs](https://www.nuget.org/packages/Microsoft.Azure.WebJobs/3.0.48) | 3.0.48 | Yes |  |  |
| 56 | [Microsoft.Extensions.Http.Polly](https://www.nuget.org/packages/Microsoft.Extensions.Http.Polly/11.0.0) | 11.0.0 | No | Reflection: looks members up at run time (`Type.GetConstructors`) |  |
| 57 | [Mono.TextTemplating](https://www.nuget.org/packages/Mono.TextTemplating/3.0.0) | 3.0.0 | No | Compiles the template's C# at run time |  |
| 58 | [RestSharp](https://www.nuget.org/packages/RestSharp/114.1.0) | 114.1.0 | Yes |  |  |
| 59 | [YamlDotNet](https://www.nuget.org/packages/YamlDotNet/18.1.0) | 18.1.0 | No | Reflection: looks members up at run time (`Type.GetFields`) | YamlDotNet's static serializer, with [Vecc.YamlDotNet.Analyzers.StaticGenerator](https://www.nuget.org/packages/Vecc.YamlDotNet.Analyzers.StaticGenerator) |
| 60 | [Google.Apis](https://www.nuget.org/packages/Google.Apis/1.78.0) | 1.78.0 | Yes |  |  |
| 61 | [Google.Apis.Core](https://www.nuget.org/packages/Google.Apis.Core/1.78.0) | 1.78.0 | No | Reflection: looks members up at run time |  |
| 62 | [Autofac](https://www.nuget.org/packages/Autofac/9.3.4) | 9.3.4 | No | Reflection: looks members up at run time (`Type.GetConstructors`) | [NetWasm.Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/NetWasm.Microsoft.Extensions.DependencyInjection), through the Microsoft.Extensions API |
| 63 | [Microsoft.AspNetCore.Metadata](https://www.nuget.org/packages/Microsoft.AspNetCore.Metadata/11.0.0) | 11.0.0 | Yes |  |  |
| 64 | [Microsoft.Extensions.Logging.ApplicationInsights](https://www.nuget.org/packages/Microsoft.Extensions.Logging.ApplicationInsights/2.23.0) | 2.23.0 | Yes |  |  |
| 65 | [Polly.Extensions.Http](https://www.nuget.org/packages/Polly.Extensions.Http/3.0.0) | 3.0.0 | Yes |  |  |
| 66 | [SharpZipLib](https://www.nuget.org/packages/SharpZipLib/1.4.2) | 1.4.2 | Yes |  |  |
| 67 | [RabbitMQ.Client](https://www.nuget.org/packages/RabbitMQ.Client/7.2.2) | 7.2.2 | Yes |  |  |
| 68 | [Microsoft.ApplicationInsights.EventCounterCollector](https://www.nuget.org/packages/Microsoft.ApplicationInsights.EventCounterCollector/2.23.0) | 2.23.0 | Yes |  |  |
| 69 | [OpenTelemetry.Extensions.Hosting](https://www.nuget.org/packages/OpenTelemetry.Extensions.Hosting/1.21.0) | 1.21.0 | No | Reflection: queries custom attributes at run time |  |
| 70 | [SQLitePCLRaw.core](https://www.nuget.org/packages/SQLitePCLRaw.core/3.0.5) | 3.0.5 | Yes |  |  |
| 71 | [Portable.BouncyCastle](https://www.nuget.org/packages/Portable.BouncyCastle/1.9.0) | 1.9.0 | Yes |  |  |
| 72 | [DnsClient](https://www.nuget.org/packages/DnsClient/1.8.0) | 1.8.0 | Yes |  |  |
| 73 | [BouncyCastle.Cryptography](https://www.nuget.org/packages/BouncyCastle.Cryptography/2.7.0) | 2.7.0 | Yes |  |  |
| 74 | [JetBrains.Annotations](https://www.nuget.org/packages/JetBrains.Annotations/2026.2.0) | 2026.2.0 | Yes |  |  |
| 75 | [MongoDB.Bson](https://www.nuget.org/packages/MongoDB.Bson/3.13.0) | 3.13.0 | No | Creates generic types at run time (`MakeGenericType`) |  |
| 76 | [MediatR](https://www.nuget.org/packages/MediatR/14.3.0) | 14.3.0 | No | Reflection: looks members up at run time (`Type.GetConstructors`) | [SwitchMediator](https://www.nuget.org/packages/Mediator.Switch) over a source-generated container such as [Jab](https://www.nuget.org/packages/Jab); see [MediatR](#mediatr) |
| 77 | [Serilog.Sinks.PeriodicBatching](https://www.nuget.org/packages/Serilog.Sinks.PeriodicBatching/5.0.0) | 5.0.0 | No | Writes nothing: it emits batches from a background task, and none ran before the program ended |  |
| 78 | [DocumentFormat.OpenXml](https://www.nuget.org/packages/DocumentFormat.OpenXml/3.5.1) | 3.5.1 | Yes |  |  |
| 79 | [Microsoft.IO.RecyclableMemoryStream](https://www.nuget.org/packages/Microsoft.IO.RecyclableMemoryStream/4.0.1) | 4.0.1 | Yes |  |  |
| 80 | [OpenTelemetry.Instrumentation.AspNetCore](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.AspNetCore/1.20.2) | 1.20.2 | No | Its build for current .NET needs ASP.NET Core's shared framework, which NetWasm does not provide |  |
| 81 | [Grpc.Net.ClientFactory](https://www.nuget.org/packages/Grpc.Net.ClientFactory/2.85.0) | 2.85.0 | No | Microsoft's DependencyInjection.Abstractions build uses `ConstructorInvoker`, which NetWasm lacks |  |
| 82 | [AWSSDK.Polly](https://www.nuget.org/packages/AWSSDK.Polly/4.0.102.3) | 4.0.102.3 | No | Reflection: looks members up at run time (`Type.GetFields`) |  |
| 83 | [MongoDB.Driver](https://www.nuget.org/packages/MongoDB.Driver/3.13.0) | 3.13.0 | Yes |  |  |
| 84 | [EntityFramework](https://www.nuget.org/packages/EntityFramework/6.5.2) | 6.5.2 | No | Reflection: looks members up at run time |  |
| 85 | [OpenTelemetry.Instrumentation.Http](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Http/1.20.2) | 1.20.2 | No | Reflection: queries custom attributes at run time |  |
| 86 | [Azure.Messaging.ServiceBus](https://www.nuget.org/packages/Azure.Messaging.ServiceBus/7.21.0) | 7.21.0 | Yes |  |  |
| 87 | [log4net](https://www.nuget.org/packages/log4net/3.5.0) | 3.5.0 | No | Reflection: creates instances by type (`Activator.CreateInstance`) |  |
| 88 | [Hangfire.Core](https://www.nuget.org/packages/Hangfire.Core/1.8.25) | 1.8.25 | No | Reflection: queries custom attributes at run time |  |
| 89 | [OpenTelemetry.Exporter.OpenTelemetryProtocol](https://www.nuget.org/packages/OpenTelemetry.Exporter.OpenTelemetryProtocol/1.21.0) | 1.21.0 | Yes |  |  |
| 90 | [NJsonSchema](https://www.nuget.org/packages/NJsonSchema/11.6.1) | 11.6.1 | No | Uses `dynamic`, whose runtime binder NetWasm lacks |  |
| 91 | [Microsoft.NET.StringTools](https://www.nuget.org/packages/Microsoft.NET.StringTools/18.12.2) | 18.12.2 | Yes |  |  |
| 92 | [AWSSDK.SecurityToken](https://www.nuget.org/packages/AWSSDK.SecurityToken/4.0.101.11) | 4.0.101.11 | Yes |  |  |
| 93 | [Azure.Core.Amqp](https://www.nuget.org/packages/Azure.Core.Amqp/1.3.1) | 1.3.1 | Yes |  |  |
| 94 | [SharpCompress](https://www.nuget.org/packages/SharpCompress/0.50.8) | 0.50.8 | Yes |  |  |
| 95 | [MongoDB.Driver.Core](https://www.nuget.org/packages/MongoDB.Driver.Core/2.30.0) | 2.30.0 | Yes |  |  |
| 96 | [Microsoft.Extensions.Azure](https://www.nuget.org/packages/Microsoft.Extensions.Azure/1.14.1) | 1.14.1 | Yes |  |  |
| 97 | [SSH.NET](https://www.nuget.org/packages/SSH.NET/2026.0.0) | 2026.0.0 | Yes |  |  |
| 98 | [HtmlAgilityPack](https://www.nuget.org/packages/HtmlAgilityPack/1.13.1) | 1.13.1 | Yes |  |  |
| 99 | [SkiaSharp](https://www.nuget.org/packages/SkiaSharp/4.156.0) | 4.156.0 | No | Calls a native library the program does not link |  |
| 100 | [AutoMapper.Extensions.Microsoft.DependencyInjection](https://www.nuget.org/packages/AutoMapper.Extensions.Microsoft.DependencyInjection/12.0.1) | 12.0.1 | No | Creates generic types at run time (`MakeGenericType`) | Mapperly; see AutoMapper |

Each row names the first thing that stops the program; a package past it may
stop on something else.

## What stops the others

- **Run-time reflection (35).** NetWasm compiles the whole program ahead of time
  and deploys no reflection metadata. Looking up a type's members at run time
  (`GetConstructors`, `GetProperties`, `GetMethod`), querying custom attributes
  the compiler cannot bound, validating options by their attributes, creating
  generic types the program does not use, and creating instances by type throw
  `PlatformNotSupportedException`, naming the operation. The common cases are
  dependency injection's constructor discovery
  (Microsoft.Extensions.DependencyInjection, Autofac and the packages that build
  on them), serializers and mappers that read an object's properties
  (Newtonsoft.Json, YamlDotNet, Google.Protobuf, AutoMapper), and libraries that
  configure themselves from attributes (NLog, OpenTelemetry, Hangfire).
- **ASP.NET Core (2).** Serilog.AspNetCore's and
  OpenTelemetry.Instrumentation.AspNetCore's builds for current .NET reference
  ASP.NET Core's shared framework, which NetWasm does not provide, so they do
  not build.
- **APIs NetWasm lacks (2).** Grpc.Net.ClientFactory reaches Microsoft's
  Microsoft.Extensions.DependencyInjection.Abstractions, whose build for
  current .NET uses `ConstructorInvoker`;
  NetWasm.Microsoft.Extensions.DependencyInjection replaces that package.
  NJsonSchema uses `dynamic`, whose runtime binder NetWasm lacks.
- **Native libraries (2).** SkiaSharp calls `libSkiaSharp`, and Dapper's quick
  start calls SQLite's native library; the program links neither.
- **Run-time code generation (1).** Mono.TextTemplating compiles the template's
  C# at run time.
- **Background work (1).** Serilog.Sinks.PeriodicBatching writes its batches from
  a background task, and none ran before the program ended, so nothing was
  written.
- **The package itself (1).** Microsoft.Data.SqlClient's .NET Standard build
  supports only Windows.

## Replacements

Each of these ran the package's quick start, rewritten for the replacement, with
the same output on NetWasm and desktop .NET.

| Instead of | Use |
| --- | --- |
| Newtonsoft.Json | System.Text.Json with source generation (`JsonSerializerContext`), which ships with NetWasm |
| Microsoft.Extensions.DependencyInjection | [NetWasm.Microsoft.Extensions.DependencyInjection](https://www.nuget.org/packages/NetWasm.Microsoft.Extensions.DependencyInjection), with the same code |
| Microsoft.Extensions.Logging | [NetWasm.Microsoft.Extensions.Logging](https://www.nuget.org/packages/NetWasm.Microsoft.Extensions.Logging), with the same code |
| Microsoft.Extensions.Http | [NetWasm.Microsoft.Extensions.Http](https://www.nuget.org/packages/NetWasm.Microsoft.Extensions.Http), with the same code |
| Autofac | NetWasm.Microsoft.Extensions.DependencyInjection, through the Microsoft.Extensions API |
| AutoMapper | [Mapperly](https://www.nuget.org/packages/Riok.Mapperly), whose mappers are source-generated |
| YamlDotNet | YamlDotNet's static serializer, with [Vecc.YamlDotNet.Analyzers.StaticGenerator](https://www.nuget.org/packages/Vecc.YamlDotNet.Analyzers.StaticGenerator) |
| MediatR | [SwitchMediator](https://www.nuget.org/packages/Mediator.Switch) over a source-generated container such as [Jab](https://www.nuget.org/packages/Jab); see below |

### MediatR

[SwitchMediator](https://www.nuget.org/packages/Mediator.Switch) has MediatR's
API, and its dispatch is source-generated, so it needs no reflection:

- Over [Jab](https://www.nuget.org/packages/Jab), a source-generated container,
  requests and notifications run unchanged. The generated mediator resolves its
  handlers through an `ISwitchMediatorServiceProvider` that reads Jab's
  generated lookups, and Jab's `RootServices` lists the notification handler
  sequences the mediator asks for.
- Registered by the application with
  NetWasm.Microsoft.Extensions.DependencyInjection (the mediator built from the
  scope's service provider, and each handler registered), requests run
  unchanged. Through its `AddMediator<T>()` extension, notifications do not run
  yet: the container does not resolve a handler sequence that only the
  extension asks for.

Mediator builds, and stops where the `AddMediator` method its generator writes
registers types that NetWasm's generated activation cannot see.
