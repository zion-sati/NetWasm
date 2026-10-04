namespace NetWasm.Compiler.ComponentModel.Tests;

public sealed class WasmCoreModuleValidatorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UsesAuthoritativeWasmToolsValidationContract(bool memory64)
    {
        var operations = new RecordingOperations();
        var target = memory64 ? ComponentTarget.Wasm64Wasi02 : ComponentTarget.Wasm32Wasi02;
        var features = "mvp,mutable-global,saturating-float-to-int,sign-extension,reference-types," +
            "multi-value,bulk-memory,exceptions,multi-memory" + (memory64 ? ",memory64" : "");

        new WasmCoreModuleValidator(operations).Validate("linked.wasm", target);

        Assert.Equal(["validate", "linked.wasm", "--features", features],
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
            new WasmCoreModuleValidator(operations).Validate("linked.wasm", ComponentTarget.Wasm32Wasi02)));
    }

    [Fact]
    public void RejectsMissingCapabilityOrPath()
    {
        Assert.Throws<ArgumentNullException>(() => new WasmCoreModuleValidator(null!));
        var operations = new RecordingOperations();
        var validator = new WasmCoreModuleValidator(operations);
        Assert.Throws<ArgumentException>(() => validator.Validate(" ", ComponentTarget.Wasm32Wasi02));
        Assert.Throws<ArgumentNullException>(() => validator.Validate("linked", null!));
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!, ComponentTarget.Wasm32Wasi02));
        Assert.Throws<ArgumentOutOfRangeException>(() => validator.Validate("linked", new("wasm128", "0.2", "utf8")));
        Assert.Empty(operations.Arguments);
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
