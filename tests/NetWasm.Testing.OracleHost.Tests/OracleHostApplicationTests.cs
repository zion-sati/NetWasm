using NetWasm.Testing.OracleHost;

namespace NetWasm.Testing.OracleHost.Tests;

public sealed class OracleHostApplicationTests
{
    [Fact]
    public void RunsEachInputThroughTheInjectedObserver()
    {
        var observer = new RecordingObserver();
        var application = new OracleHostApplication(
            new FixedInputReader(new([2, 4], IsBatched: true)),
            observer);

        var exitCode = application.Run(
            ["fixture.dll", "Fixture.Type", "Execute", "@inputs.json", "false"]);

        Assert.Equal(0, exitCode);
        Assert.Equal([2, 4], observer.Inputs);
        Assert.Equal(["Execute", "Execute"], observer.Methods);
    }

    [Fact]
    public void RejectsAnInputReaderThatCannotParseTheInput()
    {
        var application = new OracleHostApplication(
            new FixedInputReader(null),
            new RecordingObserver());

        Assert.Equal(2, application.Run(
            ["fixture.dll", "Fixture.Type", "Run", "bad", "false"]));
    }

    private sealed class FixedInputReader(OracleInputs? inputs) : IOracleInputReader
    {
        public OracleInputs? Read(string argument) => inputs;
    }

    private sealed class RecordingObserver : IOracleAssemblyObserver
    {
        public List<int> Inputs { get; } = [];
        public List<string> Methods { get; } = [];

        public Observation Observe(
            string assemblyPath,
            string typeName,
            string methodName,
            int input,
            bool typedTrace,
            string? referencePath)
        {
            Inputs.Add(input);
            Methods.Add(methodName);
            return new("value", input, null, 0, null, []);
        }
    }
}
