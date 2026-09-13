using NetWasm.Hosting.Execution;

namespace NetWasm.Hosting.Tests.Execution;

internal static class ExecutionRequestFixture
{
    internal static NetWasmExecutionRequest Create() => new(
        1,
        new string('a', 64),
        new string('b', 64),
        ["first", "", "line\nbreak"],
        [new("ZED", "last"), new("ALPHA", "first")],
        new(
            ["ZED", "ALPHA"],
            [
                new(Path.GetFullPath("writable"), "/work", NetWasmPreopenAccess.ReadWrite),
                new(Path.GetFullPath("readonly"), "/data", NetWasmPreopenAccess.ReadOnly),
            ],
            NetWasmNetworkPolicy.DenyAll,
            [NetWasmClock.Wall, NetWasmClock.Monotonic],
            true),
        [
            new("example:logging/logger@1.0.0", "imports/logger.mjs", new string('c', 64)),
            new("example:storage/cache@2.1.3", "imports/cache.mjs", new string('d', 64)),
        ]);
}

internal sealed class ExecutionRequestValidationStub(Action<NetWasmExecutionRequest> validate)
    : INetWasmExecutionRequestValidator
{
    public void Validate(NetWasmExecutionRequest request) => validate(request);
}
