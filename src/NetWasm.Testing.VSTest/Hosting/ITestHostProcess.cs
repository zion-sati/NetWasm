namespace NetWasm.Testing.VSTest.Hosting;

internal interface ITestHostProcess : IDisposable
{
    int Id { get; }

    void Terminate();
}
