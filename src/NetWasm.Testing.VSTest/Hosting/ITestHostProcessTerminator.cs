namespace NetWasm.Testing.VSTest.Hosting;

internal interface ITestHostProcessTerminator
{
    void Terminate(
        Func<bool> hasExited,
        Action kill,
        Action waitForExit);
}
