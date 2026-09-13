using Microsoft.VisualStudio.TestPlatform.ObjectModel;

namespace NetWasm.Testing.VSTest.Hosting;

internal interface ITestHostStartInfoBuilder
{
    TestProcessStartInfo Build(
        string dotnetPath,
        PortableTestHostAssets assets,
        string workingDirectory,
        IDictionary<string, string?>? environmentVariables,
        TestRunnerConnectionInfo connectionInfo);
}
