using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;

namespace NetWasm.Compiler.ComponentModel;

public interface IExternalToolRunner
{
    ToolResult Run(string executable, IEnumerable<string> arguments);
}

public interface IProcessExecution
{
    ToolResult Execute(ExternalToolInvocation invocation);
}

public interface IProcessFactory
{
    IProcessSession Create(string executable);
}

public interface IProcessSession : IDisposable
{
    IProcessEnvironmentTarget Environment { get; }
    IProcessArgumentTarget Arguments { get; }
    IProcessStartTarget Starter { get; }
    IProcessStandardOutputTarget StandardOutput { get; }
    IProcessStandardErrorTarget StandardError { get; }
    IProcessWaitTarget Waiter { get; }
    IProcessExitCodeTarget ExitCode { get; }
}

public interface IProcessArgumentTarget
{
    void Append(string argument);
}

public interface IProcessStartTarget
{
    void Start();
}

public interface IProcessStandardOutputTarget
{
    Task<string> ReadAsync();
}

public interface IProcessStandardErrorTarget
{
    Task<string> ReadAsync();
}

public interface IProcessWaitTarget
{
    void Wait();
}

public interface IProcessExitCodeTarget
{
    int Read();
}

public interface IProcessArgumentAppender
{
    void Append(IProcessSession process, string argument);
}

public interface IProcessStarter
{
    void Start(IProcessSession process);
}

public interface IStandardOutputReader
{
    Task<string> ReadAsync(IProcessSession process);
}

public interface IStandardErrorReader
{
    Task<string> ReadAsync(IProcessSession process);
}

public interface IProcessWaiter
{
    void Wait(IProcessSession process);
}

public interface IProcessExitCodeReader
{
    int Read(IProcessSession process);
}

public sealed class ProcessExternalToolRunner : IExternalToolRunner, IConfiguredExternalToolRunner
{
    private readonly IProcessExecution _execution;

    public ProcessExternalToolRunner(IProcessExecution execution)
    {
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
    }

    public ToolResult Run(string executable, IEnumerable<string> arguments)
    {
        RequireExecutable(executable, nameof(executable));
        ArgumentNullException.ThrowIfNull(arguments);
        return Run(new(executable, [.. arguments], []));
    }

    public ToolResult Run(ExternalToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        RequireExecutable(invocation.Executable, nameof(invocation));
        RequireExplicit(invocation.Arguments, "tool arguments");
        RequireExplicit(invocation.EnvironmentVariablePrefixesToRemove,
            "environment variable prefixes");
        foreach (var argument in invocation.Arguments)
        {
            if (argument is null)
            {
                throw new ArgumentException(
                    "tool arguments cannot contain null values",
                    nameof(invocation));
            }
        }
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prefix in invocation.EnvironmentVariablePrefixesToRemove)
        {
            if (string.IsNullOrWhiteSpace(prefix) || !prefixes.Add(prefix))
            {
                throw new ArgumentException(
                    "environment variable removal prefixes must be nonempty and unique",
                    nameof(invocation));
            }
        }
        return _execution.Execute(invocation);
    }

    private static void RequireExecutable(string executable, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(executable))
        {
            throw new ArgumentException(
                "tool executable cannot be empty",
                parameterName);
        }
    }

    private static void RequireExplicit<T>(ImmutableArray<T> values, string label)
    {
        if (values.IsDefault)
        {
            throw new ArgumentException($"{label} must be explicit", nameof(values));
        }
    }
}
