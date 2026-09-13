using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace NetWasm.Testing.VSTest.Hosting;

internal interface ITestHostProcessStarter
{
    ITestHostProcess Start(
        TestProcessStartInfo startInfo,
        Action<string> standardOutput,
        Action<string> standardError,
        Action<int, int> exited);
}
