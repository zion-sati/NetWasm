namespace NetWasm.Testing.VSTest.Hosting;

internal interface ITestHostProcessAttacher
{
    ITestHostProcess Attach(int processId, Action<int, int> exited);
}
