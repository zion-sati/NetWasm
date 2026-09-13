using System.ComponentModel;
using System.Diagnostics;

namespace NetWasm.Testing.VSTest.Hosting;

internal sealed class TestHostProcessAttacher : ITestHostProcessAttacher
{
    private readonly ITestHostProcessTerminator _terminator;

    internal TestHostProcessAttacher(ITestHostProcessTerminator terminator)
    {
        _terminator = terminator ?? throw new ArgumentNullException(nameof(terminator));
    }

    public ITestHostProcess Attach(int processId, Action<int, int> exited)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        ArgumentNullException.ThrowIfNull(exited);
        try
        {
            return new TestHostProcess(Process.GetProcessById(processId), exited, _terminator);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or Win32Exception)
        {
            throw new InvalidOperationException(
                "The custom NetWasm testhost process is unavailable.",
                exception);
        }
    }
}
