using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Metadata;
using NetWasm.Testing.CompilerHost;

var workerOverride = 0;
if (args is ["--workers", var count, ..] &&
    (!int.TryParse(count, out workerOverride) || workerOverride < 1))
{
    Console.Error.WriteLine("worker count must be positive");
    return 2;
}
var hostArgs = workerOverride == 0 ? args : args[2..];
var sequenceMode = hostArgs is ["--sequence", _, _];
if (!sequenceMode && hostArgs.Length != 2)
{
    Console.Error.WriteLine(
        "usage: NetWasm.Testing.CompilerHost <request.json> <response.json> | " +
        "--sequence <sequence.json> <receipt.json> " +
        "[test-only prefix: --workers <count>]");
    return 2;
}

try
{
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };
    var registrations = new ServiceCollection()
        .AddNetWasmCompiler()
        .AddSingleton<ConsoleCompilerProgressReporter>()
        .AddSingleton<ICompilerProgressReporter>(provider =>
            provider.GetRequiredService<ConsoleCompilerProgressReporter>());
    if (workerOverride > 0)
        registrations.Replace(ServiceDescriptor.Singleton(
            new CompilerParallelism(workerOverride)));
    using var services = registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    var application = new CompilerHostApplication(
        services.GetRequiredService<INetWasmCompiler>(),
        services.GetRequiredService<IMetadataCompilationLoader>());
    if (!sequenceMode)
    {
        var request = new CompilerHostRequestReader(
            new CompilerHostRequestDeserializer(jsonOptions)).Read(hostArgs[0]);
        return application.Run(request, hostArgs[1]);
    }
    var sequence = JsonSerializer.Deserialize<CompilationSequenceRequest>(
        File.ReadAllText(hostArgs[1]),
        jsonOptions)
        ?? throw new InvalidOperationException("compiler sequence was empty");
    return new CompilerHostSequenceApplication(application).Run(sequence, hostArgs[2]);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
