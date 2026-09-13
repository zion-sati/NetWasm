using System;
using System.Threading.Tasks;

namespace NetWasm.Compiler.ComponentModel;

public sealed class SystemProcessExecution : IProcessExecution
{
    private readonly IProcessFactory _factory;
    private readonly IProcessArgumentAppender _argumentAppender;
    private readonly IProcessStarter _starter;
    private readonly IStandardOutputReader _standardOutputReader;
    private readonly IStandardErrorReader _standardErrorReader;
    private readonly IProcessWaiter _waiter;
    private readonly IProcessExitCodeReader _exitCodeReader;

    public SystemProcessExecution(
        IProcessFactory factory,
        IProcessArgumentAppender argumentAppender,
        IProcessStarter starter,
        IStandardOutputReader standardOutputReader,
        IStandardErrorReader standardErrorReader,
        IProcessWaiter waiter,
        IProcessExitCodeReader exitCodeReader)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(argumentAppender);
        ArgumentNullException.ThrowIfNull(starter);
        ArgumentNullException.ThrowIfNull(standardOutputReader);
        ArgumentNullException.ThrowIfNull(standardErrorReader);
        ArgumentNullException.ThrowIfNull(waiter);
        ArgumentNullException.ThrowIfNull(exitCodeReader);

        _factory = factory;
        _argumentAppender = argumentAppender;
        _starter = starter;
        _standardOutputReader = standardOutputReader;
        _standardErrorReader = standardErrorReader;
        _waiter = waiter;
        _exitCodeReader = exitCodeReader;
    }

    public ToolResult Execute(ExternalToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        using var process = _factory.Create(invocation.Executable);
        foreach (var prefix in invocation.EnvironmentVariablePrefixesToRemove)
        {
            process.Environment.RemoveByPrefix(prefix);
        }
        foreach (var argument in invocation.Arguments)
        {
            _argumentAppender.Append(process, argument);
        }

        try
        {
            _starter.Start(process);
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                          System.ComponentModel.Win32Exception)
        {
            throw ComponentException.Tool(
                $"unable to start executable '{invocation.Executable}': {exception.Message}");
        }

        var standardOutput = _standardOutputReader.ReadAsync(process);
        var standardError = _standardErrorReader.ReadAsync(process);
        _waiter.Wait(process);
        Task.WaitAll(standardOutput, standardError);
        return new ToolResult(
            _exitCodeReader.Read(process),
            standardOutput.Result,
            standardError.Result);
    }
}
