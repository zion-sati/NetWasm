using System;
using Microsoft.Extensions.DependencyInjection;
using NetWasm.Compiler.ComponentModel.Node;

namespace NetWasm.Compiler.ComponentModel.Process;

internal static class ProcessServiceCollectionExtensions
{
    public static IServiceCollection AddCompilerComponentModelProcess(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IProcessFactory, SystemProcessFactory>();
        services.AddSingleton<IProcessArgumentAppender, SystemProcessArgumentAppender>();
        services.AddSingleton<IProcessStarter, SystemProcessStarter>();
        services.AddSingleton<IStandardOutputReader, SystemStandardOutputReader>();
        services.AddSingleton<IStandardErrorReader, SystemStandardErrorReader>();
        services.AddSingleton<IProcessWaiter, SystemProcessWaiter>();
        services.AddSingleton<IProcessExitCodeReader, SystemProcessExitCodeReader>();
        services.AddSingleton<IProcessExecution, SystemProcessExecution>();
        services.AddSingleton<IExternalToolRunner, ProcessExternalToolRunner>();
        services.AddSingleton<IConfiguredExternalToolRunner, ProcessExternalToolRunner>();
        services.AddSingleton<ISystemNodeCommandRunner, SystemNodeCommandRunner>();
        services.AddSingleton<IBinaryenToolRunner, ProcessBinaryenToolRunner>();
        return services;
    }
}
