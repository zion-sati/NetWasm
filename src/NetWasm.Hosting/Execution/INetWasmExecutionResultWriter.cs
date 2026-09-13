namespace NetWasm.Hosting.Execution;

public interface INetWasmExecutionResultWriter
{
    byte[] Write(NetWasmExecutionResult result);
}
