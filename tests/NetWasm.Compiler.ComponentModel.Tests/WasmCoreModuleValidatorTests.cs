namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class WasmCoreModuleValidatorTests
{
    [Fact]
    public void UsesAuthoritativeWasmToolsValidationContract()
    {
        var operations = new RecordingOperations();

        new WasmCoreModuleValidator(operations).Validate("linked.wasm");

        Assert.Equal(["validate", "linked.wasm", "--features", "all"],
            operations.Arguments);
        Assert.Equal("validate the linked core module", operations.Operation);
    }

    [Fact]
    public void PropagatesValidationFailure()
    {
        var operations = new RecordingOperations
        {
            Failure = new InvalidOperationException("validation failed"),
        };

        Assert.Same(operations.Failure, Assert.Throws<InvalidOperationException>(() =>
            new WasmCoreModuleValidator(operations).Validate("linked.wasm")));
    }

    [Fact]
    public void RejectsMissingCapabilityOrPath()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleValidator(null!));
        Assert.Throws<ArgumentException>(() =>
            new WasmCoreModuleValidator(new RecordingOperations()).Validate(" "));
    }

    private sealed class RecordingOperations : IComponentPackageOperationRunner
    {
        public Exception? Failure { get; init; }
        public string[] Arguments { get; private set; } = [];
        public string? Operation { get; private set; }

        public void Run(IEnumerable<string> arguments, string operation)
        {
            Arguments = arguments.ToArray();
            Operation = operation;
            if (Failure is not null) throw Failure;
        }
    }
}
