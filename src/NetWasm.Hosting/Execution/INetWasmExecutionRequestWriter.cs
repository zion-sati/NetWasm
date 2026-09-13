namespace NetWasm.Hosting.Execution;

public interface INetWasmExecutionRequestWriter
{
    byte[] Write(NetWasmExecutionRequest request);
}
