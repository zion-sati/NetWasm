using System.ComponentModel;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class SystemProcessExecutionTests
{
    [Fact]
    public void ExecuteAppliesConfigurationBeforeStartingAndObservingTheProcess()
    {
        var events = new List<string>();
        var session = new RecordingProcessSession(events);
        var execution = CreateExecution(events, session, exitCode: 17);

        var result = execution.Execute(new(
            "tool",
            ["--one", "--two"],
            ["NODE_", "NPM_CONFIG_"]));

        Assert.Equal(new ToolResult(17, "output", "error"), result);
        Assert.Equal(
            [
                "factory:tool",
                "environment:NODE_",
                "environment:NPM_CONFIG_",
                "argument:--one",
                "argument:--two",
                "start",
                "stdout",
                "stderr",
                "wait",
                "exit",
                "dispose",
            ],
            events);
    }

    [Fact]
    public void ExecuteRejectsANullInvocationBeforeCreatingAProcess()
    {
        var events = new List<string>();
        var execution = CreateExecution(events, new RecordingProcessSession(events));

        Assert.Throws<ArgumentNullException>(() => execution.Execute(null!));
        Assert.Empty(events);
    }

    [Theory]
    [MemberData(nameof(KnownStartFailures))]
    public void ExecuteWrapsKnownStartFailuresAndDisposesTheProcess(Exception failure)
    {
        var events = new List<string>();
        var execution = CreateExecution(
            events,
            new RecordingProcessSession(events),
            startFailure: failure);

        var exception = Assert.Throws<CompilerException>(() => execution.Execute(new(
            "missing-tool",
            [],
            [])));

        Assert.Equal(DiagnosticCode.ComponentToolchain, exception.Diagnostic.Code);
        Assert.Contains("missing-tool", exception.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains(failure.Message, exception.Diagnostic.Message, StringComparison.Ordinal);
        Assert.Equal(["factory:missing-tool", "start", "dispose"], events);
    }

    public static TheoryData<Exception> KnownStartFailures => new()
    {
        new InvalidOperationException("invalid start"),
        new Win32Exception("native start"),
    };

    [Fact]
    public void ExecutePreservesUnknownStartFailuresAndDisposesTheProcess()
    {
        var events = new List<string>();
        var execution = CreateExecution(
            events,
            new RecordingProcessSession(events),
            startFailure: new ArgumentException("bad start"));

        Assert.Throws<ArgumentException>(() => execution.Execute(new("tool", [], [])));
        Assert.Equal(["factory:tool", "start", "dispose"], events);
    }

    [Fact]
    public void ConstructorRejectsEveryMissingProcessCapability()
    {
        var events = new List<string>();
        var session = new RecordingProcessSession(events);
        var factory = new RecordingProcessFactory(events, session);
        var arguments = new RecordingProcessArgumentAppender(events);
        var starter = new RecordingProcessStarter(events);
        var standardOutput = new RecordingStandardOutputReader(events);
        var standardError = new RecordingStandardErrorReader(events);
        var waiter = new RecordingProcessWaiter(events);
        var exitCode = new RecordingProcessExitCodeReader(events, 0);

        Assert.Throws<ArgumentNullException>(() => new SystemProcessExecution(
            null!, arguments, starter, standardOutput, standardError, waiter, exitCode));
        Assert.Throws<ArgumentNullException>(() => new SystemProcessExecution(
            factory, null!, starter, standardOutput, standardError, waiter, exitCode));
        Assert.Throws<ArgumentNullException>(() => new SystemProcessExecution(
            factory, arguments, null!, standardOutput, standardError, waiter, exitCode));
        Assert.Throws<ArgumentNullException>(() => new SystemProcessExecution(
            factory, arguments, starter, null!, standardError, waiter, exitCode));
        Assert.Throws<ArgumentNullException>(() => new SystemProcessExecution(
            factory, arguments, starter, standardOutput, null!, waiter, exitCode));
        Assert.Throws<ArgumentNullException>(() => new SystemProcessExecution(
            factory, arguments, starter, standardOutput, standardError, null!, exitCode));
        Assert.Throws<ArgumentNullException>(() => new SystemProcessExecution(
            factory, arguments, starter, standardOutput, standardError, waiter, null!));
    }

    private static SystemProcessExecution CreateExecution(
        List<string> events,
        RecordingProcessSession session,
        int exitCode = 0,
        Exception? startFailure = null) =>
        new(
            new RecordingProcessFactory(events, session),
            new RecordingProcessArgumentAppender(events),
            new RecordingProcessStarter(events, startFailure),
            new RecordingStandardOutputReader(events),
            new RecordingStandardErrorReader(events),
            new RecordingProcessWaiter(events),
            new RecordingProcessExitCodeReader(events, exitCode));

    private sealed class RecordingProcessSession(List<string> events) : IProcessSession
    {
        public IProcessEnvironmentTarget Environment { get; } =
            new RecordingProcessEnvironmentTarget(events);
        public IProcessArgumentTarget Arguments { get; } = new NoopArgumentTarget();
        public IProcessStartTarget Starter { get; } = new NoopStartTarget();
        public IProcessStandardOutputTarget StandardOutput { get; } = new NoopOutputTarget();
        public IProcessStandardErrorTarget StandardError { get; } = new NoopErrorTarget();
        public IProcessWaitTarget Waiter { get; } = new NoopWaitTarget();
        public IProcessExitCodeTarget ExitCode { get; } = new NoopExitCodeTarget();

        public void Dispose() => events.Add("dispose");
    }

    private sealed class RecordingProcessEnvironmentTarget(List<string> events) :
        IProcessEnvironmentTarget
    {
        public void RemoveByPrefix(string prefix) => events.Add($"environment:{prefix}");
    }

    private sealed class RecordingProcessFactory(
        List<string> events,
        IProcessSession session) : IProcessFactory
    {
        public IProcessSession Create(string executable)
        {
            events.Add($"factory:{executable}");
            return session;
        }
    }

    private sealed class RecordingProcessArgumentAppender(List<string> events) :
        IProcessArgumentAppender
    {
        public void Append(IProcessSession process, string argument) =>
            events.Add($"argument:{argument}");
    }

    private sealed class RecordingProcessStarter(
        List<string> events,
        Exception? failure = null) : IProcessStarter
    {
        public void Start(IProcessSession process)
        {
            events.Add("start");
            if (failure is not null)
            {
                throw failure;
            }
        }
    }

    private sealed class RecordingStandardOutputReader(List<string> events) :
        IStandardOutputReader
    {
        public Task<string> ReadAsync(IProcessSession process)
        {
            events.Add("stdout");
            return Task.FromResult("output");
        }
    }

    private sealed class RecordingStandardErrorReader(List<string> events) :
        IStandardErrorReader
    {
        public Task<string> ReadAsync(IProcessSession process)
        {
            events.Add("stderr");
            return Task.FromResult("error");
        }
    }

    private sealed class RecordingProcessWaiter(List<string> events) : IProcessWaiter
    {
        public void Wait(IProcessSession process) => events.Add("wait");
    }

    private sealed class RecordingProcessExitCodeReader(List<string> events, int exitCode) :
        IProcessExitCodeReader
    {
        public int Read(IProcessSession process)
        {
            events.Add("exit");
            return exitCode;
        }
    }

    private sealed class NoopArgumentTarget : IProcessArgumentTarget
    {
        public void Append(string argument)
        {
        }
    }

    private sealed class NoopStartTarget : IProcessStartTarget
    {
        public void Start()
        {
        }
    }

    private sealed class NoopOutputTarget : IProcessStandardOutputTarget
    {
        public Task<string> ReadAsync() => Task.FromResult(string.Empty);
    }

    private sealed class NoopErrorTarget : IProcessStandardErrorTarget
    {
        public Task<string> ReadAsync() => Task.FromResult(string.Empty);
    }

    private sealed class NoopWaitTarget : IProcessWaitTarget
    {
        public void Wait()
        {
        }
    }

    private sealed class NoopExitCodeTarget : IProcessExitCodeTarget
    {
        public int Read() => 0;
    }
}
