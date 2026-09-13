using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using NetWasm.Compiler;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Metadata;
using NetWasm.Testing.CompilerHost;

if (args.Length != 2)
{
    Console.Error.WriteLine(
        "usage: NetWasm.Testing.CompilerHost <request.json> <response.json>");
    return 2;
}

try
{
    var request = new CompilerHostRequestReader(
        new CompilerHostRequestDeserializer(new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        })).Read(args[0]);
    using var services = new ServiceCollection()
        .AddNetWasmCompiler()
        .AddSingleton<ConsoleCompilerProgressReporter>()
        .AddSingleton<ICompilerProgressReporter>(provider =>
            provider.GetRequiredService<ConsoleCompilerProgressReporter>())
        .BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    return new CompilerHostApplication(
        services.GetRequiredService<INetWasmCompiler>(),
        services.GetRequiredService<IMetadataCompilationLoader>())
        .Run(request, args[1]);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
