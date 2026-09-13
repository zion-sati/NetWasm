using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Cli;

internal interface ICompilerCli
{
    int Run(string[] arguments);
}

internal interface ICompilerCliCommand
{
    string Name { get; }

    int Run(string[] arguments);
}

internal sealed class CompilerCli(IEnumerable<ICompilerCliCommand> commands) : ICompilerCli
{
    private readonly Dictionary<string, ICompilerCliCommand> _commands =
        commands.ToDictionary(command => command.Name, StringComparer.Ordinal);

    public int Run(string[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var name = arguments.Length != 0 && _commands.ContainsKey(arguments[0])
            ? arguments[0]
            : CompileCliCommand.CommandName;
        var commandArguments = name == CompileCliCommand.CommandName
            ? arguments
            : arguments[1..];
        return _commands[name].Run(commandArguments);
    }
}

internal static class CompilerCliServiceCollectionExtensions
{
    public static IServiceCollection AddNetWasmCompilerCli(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ICompilerCliCommand, CompileCliCommand>();
        services.AddSingleton<ICompilerCliCommand, ComponentizeCliCommand>();
        services.AddSingleton<ITextFileReader, SystemTextFileReader>();
        services.AddSingleton<ITextFileWriter, SystemTextFileWriter>();
        services.AddSingleton<IBinaryFileWriter, SystemBinaryFileWriter>();
        services.AddSingleton<IComponentManifestInputReader,
            ComponentManifestInputReader>();
        services.AddSingleton<ICompilerCli, CompilerCli>();
        return services;
    }
}
