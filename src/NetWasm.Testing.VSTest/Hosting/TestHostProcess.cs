using System.Diagnostics;

namespace NetWasm.Testing.VSTest.Hosting;

internal sealed class TestHostProcess : ITestHostProcess
{
    private readonly Process _process;
    private readonly Action<int, int> _exited;
    private readonly ITestHostProcessTerminator _terminator;

    internal TestHostProcess(
        Process process,
        Action<int, int> exited,
        ITestHostProcessTerminator terminator)
    {
        _process = process ?? throw new ArgumentNullException(nameof(process));
        _exited = exited ?? throw new ArgumentNullException(nameof(exited));
        _terminator = terminator ?? throw new ArgumentNullException(nameof(terminator));
        Id = process.Id;
        process.Exited += OnExited;
        process.EnableRaisingEvents = true;
    }

    public int Id { get; }

    public void Terminate() => _terminator.Terminate(
        () => _process.HasExited,
        () => _process.Kill(entireProcessTree: true),
        _process.WaitForExit);

    public void Dispose()
    {
        _process.Exited -= OnExited;
        _process.Dispose();
    }

    private void OnExited(object? sender, EventArgs eventArgs)
    {
        _process.WaitForExit();
        _exited(Id, _process.ExitCode);
    }
}
