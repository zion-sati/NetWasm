using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Adapter;
using Microsoft.VisualStudio.TestPlatform.ObjectModel.Logging;

namespace NetWasm.Testing.VSTest.Tests.Adapter;

[FileExtension(".dll")]
[DefaultExecutorUri(SyntheticNetWasmTestExecutor.ExecutorUri)]
public sealed class SyntheticNetWasmTestDiscoverer : ITestDiscoverer
{
    public void DiscoverTests(
        IEnumerable<string> sources,
        IDiscoveryContext discoveryContext,
        IMessageLogger logger,
        ITestCaseDiscoverySink discoverySink)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(discoverySink);
        foreach (var source in sources)
        {
            discoverySink.SendTestCase(SyntheticNetWasmTestExecutor.CreateTestCase(source));
        }
    }
}

[ExtensionUri(ExecutorUri)]
public sealed class SyntheticNetWasmTestExecutor : ITestExecutor
{
    internal const string ExecutorUri = "executor://NetWasm/GenericVSTestContract/v1";

    public void RunTests(
        IEnumerable<TestCase>? tests,
        IRunContext? runContext,
        IFrameworkHandle? frameworkHandle)
    {
        ArgumentNullException.ThrowIfNull(tests);
        ArgumentNullException.ThrowIfNull(frameworkHandle);
        foreach (var test in tests)
        {
            Record(test, frameworkHandle);
        }
    }

    public void RunTests(
        IEnumerable<string>? sources,
        IRunContext? runContext,
        IFrameworkHandle? frameworkHandle)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(frameworkHandle);
        foreach (var source in sources)
        {
            Record(CreateTestCase(source), frameworkHandle);
        }
    }

    public void Cancel()
    {
    }

    internal static TestCase CreateTestCase(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        return new TestCase(
            "NetWasm.GenericVSTest.Contract",
            new Uri(ExecutorUri),
            source);
    }

    private static void Record(TestCase test, IFrameworkHandle frameworkHandle)
    {
        var failed = Environment.GetEnvironmentVariable("NETWASM_VSTEST_FAIL_CANARY") == "1";
        var outcome = failed ? TestOutcome.Failed : TestOutcome.Passed;
        frameworkHandle.RecordStart(test);
        frameworkHandle.RecordResult(new TestResult(test)
        {
            Outcome = outcome,
            ErrorMessage = failed ? "Synthetic failing canary." : null,
        });
        frameworkHandle.RecordEnd(test, outcome);
    }
}
