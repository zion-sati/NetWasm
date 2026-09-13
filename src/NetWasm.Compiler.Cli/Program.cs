using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

internal static class Program
{
    internal static int Main(string[] args)
    {
        try
        {
            using var services = new ServiceCollection()
                .AddNetWasmCompiler()
                .AddNetWasmCompilerCli()
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true,
                });
            return services.GetRequiredService<ICompilerCli>().Run(args);
        }
        catch (CompilerException exception)
        {
            Console.Error.WriteLine(exception.Diagnostic);
            return 1;
        }
    }
}
