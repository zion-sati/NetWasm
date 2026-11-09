namespace NetWasm.Compiler.Cli.Tests.TestSupport;

// Tests that redirect the process-wide Console (or set process environment variables the
// compiler reads) run in this collection, which xUnit never runs beside another test.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessConsoleTests
{
    public const string Name = "Process console";
}
