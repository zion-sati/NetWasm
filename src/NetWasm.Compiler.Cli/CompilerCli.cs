using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetWasm.Compiler.ComponentModel.Optimization;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.NativeInterop;

namespace NetWasm.Compiler.Cli;

internal interface ICompilerCli
{
    int Run(string[] arguments);
}

internal interface ICompilerCliCommand
{
    // The first argument that selects the command; compile has none.
    string Name { get; }

    FrozenSet<string> Options { get; }

    int Run(NwcArguments arguments);
}

// Selects the command and reads its arguments as csc does: the banner first,
// unless -nologo, then every argument error, or -help's summary, or the command.
internal sealed class CompilerCli(
    IEnumerable<ICompilerCliCommand> commands,
    ITextFileReader files,
    IConsole console,
    ILogger<CompilerCli> logger) : ICompilerCli
{
    private readonly Dictionary<string, ICompilerCliCommand> _commands =
        commands.ToDictionary(command => command.Name, StringComparer.Ordinal);
    private readonly ITextFileReader _files = files ?? throw new ArgumentNullException(nameof(files));
    private readonly IConsole _console = console ?? throw new ArgumentNullException(nameof(console));
    private readonly ILogger<CompilerCli> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public int Run(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var named = arguments.Length != 0 && arguments[0].Length != 0 && _commands.ContainsKey(arguments[0]);
        var command = _commands[named ? arguments[0] : string.Empty];
        var read = NwcArgumentReader.Read(named ? arguments[1..] : arguments, command.Options, _files);
        var common = new NwcOptionValues(read);
        if (!common.Flag("nologo"))
        {
            NwcBanner.Write(_console, NwcBanner.InformationalVersion());
        }
        foreach (var error in read.Errors)
        {
            _logger.Report(new CompilerDiagnostic(DiagnosticCode.InvalidCommandLine, error));
        }
        if (read.Errors.Length != 0)
        {
            return 1;
        }
        if (common.Flag("help") | common.Flag("?"))
        {
            NwcHelp.Write(_console);
            return 0;
        }
        return command.Run(read);
    }
}

internal static class CompilerCliServiceCollectionExtensions
{
    public static IServiceCollection AddNetWasmCompilerCli(this IServiceCollection services, IConsole console)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(console);
        services.AddSingleton(console);
        services.AddLogging(logging => logging
            .AddProvider(new NwcConsoleLoggerProvider(console))
            .AddFilter<NwcConsoleLoggerProvider>(NwcConsoleLoggerProvider.Shows));
        services.AddSingleton<ICompilerCliCommand, CompileCliCommand>();
        services.AddSingleton<ICompilerCliCommand, ComponentizeCliCommand>();
        services.AddSingleton<INwcCompilerSessionFactory, NwcCompilerSessionFactory>();
        services.AddSingleton<IManagedEntryPointReader, ManagedEntryPointReader>();
        services.AddSingleton<ICompilerArtifactWriter, CompilerArtifactWriter>();
        services.AddSingleton<IManagedCompilerOptimizationOverridesParser, ManagedCompilerOptimizationOverridesParser>();
        services.AddSingleton<ITextFileReader, SystemTextFileReader>();
        services.AddSingleton<ITextFileWriter, SystemTextFileWriter>();
        services.AddSingleton<IBinaryFileWriter, SystemBinaryFileWriter>();
        services.AddSingleton<IFileDeleter, SystemFileDeleter>();
        services.AddSingleton<INativeCallbackSupportArtifactValidator,
            NativeCallbackSupportArtifactValidator>();
        services.AddSingleton<IComponentManifestInputReader,
            ComponentManifestInputReader>();
        services.AddSingleton<ICompilerCli, CompilerCli>();
        return services;
    }
}
