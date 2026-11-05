using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Cli;

internal static class Program
{
    internal static int Main(string[] args)
    {
        var result = Run(args, new SystemConsole());
        // A plain account of the compile's garbage collection, for tuning.
        if (Environment.GetEnvironmentVariable("NETWASM_GC_STATS") == "1")
        {
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            Console.Error.WriteLine(
                $"gc: gen0 {GC.CollectionCount(0)} gen1 {GC.CollectionCount(1)} gen2 {GC.CollectionCount(2)}, " +
                $"pause {GC.GetTotalPauseDuration().TotalMilliseconds:F0} ms, allocated {GC.GetTotalAllocatedBytes() >> 20} MB, " +
                $"peak working set {process.PeakWorkingSet64 >> 20} MB");
        }
        return result;
    }

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
            if (exception.Diagnostic.Code == DiagnosticCode.InvalidCommandLine)
            {
                console.WriteLine(NwcHelp.Hint);
            }
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
