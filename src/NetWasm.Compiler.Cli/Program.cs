using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

internal static class Program
{
    internal static int Main(string[] args) => Run(args, new SystemConsole());

    // Runs one command and reports its failure as csc does: a diagnostic line,
    // then exit code 1.
    internal static int Run(string[] args, IConsole console)
    {
        using var services = new ServiceCollection()
            .AddNetWasmCompiler()
            .AddNetWasmCompilerCli(console)
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true,
            });
        var logger = services.GetRequiredService<ILogger<CompilerCli>>();
        try
        {
            return services.GetRequiredService<ICompilerCli>().Run(args);
        }
        catch (CompilerException exception)
        {
            logger.Report(exception.Diagnostic);
        }
#pragma warning disable CA1031 // Any other failure is reported as a diagnostic, as csc does.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            logger.Report(new CompilerDiagnostic(DiagnosticCode.HostFailure, exception.Message));
        }
        return 1;
    }
}
