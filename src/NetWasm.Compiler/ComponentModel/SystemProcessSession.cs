using System;
using System.Threading.Tasks;
using SystemProcess = System.Diagnostics.Process;

namespace NetWasm.Compiler.ComponentModel;

internal sealed class SystemProcessSession : IProcessSession
{
    private readonly SystemProcess _process;

    public IProcessEnvironmentTarget Environment { get; }
    public IProcessArgumentTarget Arguments { get; }
    public IProcessStartTarget Starter { get; }
    public IProcessStandardOutputTarget StandardOutput { get; }
    public IProcessStandardErrorTarget StandardError { get; }
    public IProcessWaitTarget Waiter { get; }
    public IProcessExitCodeTarget ExitCode { get; }

    public SystemProcessSession(SystemProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);
        _process = process;
        Environment = new SystemProcessEnvironmentTarget(process.StartInfo.Environment);
        Arguments = new ProcessArgumentTarget(process);
        Starter = new ProcessStartTarget(process);
        StandardOutput = new ProcessStandardOutputTarget(process);
        StandardError = new ProcessStandardErrorTarget(process);
        Waiter = new ProcessWaitTarget(process);
        ExitCode = new ProcessExitCodeTarget(process);
    }

    public void Dispose() => _process.Dispose();

    private sealed class ProcessArgumentTarget(SystemProcess process) :
        IProcessArgumentTarget
    {
        public void Append(string argument) =>
            process.StartInfo.ArgumentList.Add(argument);
    }

    private sealed class ProcessStartTarget(SystemProcess process) : IProcessStartTarget
    {
        public void Start() => process.Start();
    }

    private sealed class ProcessStandardOutputTarget(SystemProcess process) :
        IProcessStandardOutputTarget
    {
        public Task<string> ReadAsync() => process.StandardOutput.ReadToEndAsync();
    }

    private sealed class ProcessStandardErrorTarget(SystemProcess process) :
        IProcessStandardErrorTarget
    {
        public Task<string> ReadAsync() => process.StandardError.ReadToEndAsync();
    }

    private sealed class ProcessWaitTarget(SystemProcess process) : IProcessWaitTarget
    {
        public void Wait() => process.WaitForExit();
    }

    private sealed class ProcessExitCodeTarget(SystemProcess process) : IProcessExitCodeTarget
    {
        public int Read() => process.ExitCode;
    }
}
