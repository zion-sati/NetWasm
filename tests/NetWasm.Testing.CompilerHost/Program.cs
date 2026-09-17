using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using NetWasm.Compiler;
using NetWasm.Compiler.Diagnostics;
using NetWasm.Compiler.Metadata;
using NetWasm.Testing.CompilerHost;

var sequenceMode = args is ["--sequence", _, _];
if (!sequenceMode && args.Length != 2)
{
    Console.Error.WriteLine(
        "usage: NetWasm.Testing.CompilerHost <request.json> <response.json> | " +
        "--sequence <sequence.json> <receipt.json>");
    return 2;
}

try
{
    var jsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };
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
    var application = new CompilerHostApplication(
        services.GetRequiredService<INetWasmCompiler>(),
        services.GetRequiredService<IMetadataCompilationLoader>());
    if (!sequenceMode)
    {
        var request = new CompilerHostRequestReader(
            new CompilerHostRequestDeserializer(jsonOptions)).Read(args[0]);
        return application.Run(request, args[1]);
    }
    var sequence = JsonSerializer.Deserialize<CompilationSequenceRequest>(
        File.ReadAllText(args[1]),
        jsonOptions)
        ?? throw new InvalidOperationException("compiler sequence was empty");
    return new CompilerHostSequenceApplication(application).Run(sequence, args[2]);
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception);
    return 1;
}
