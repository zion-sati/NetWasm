namespace NetWasm.Hosting.Execution;

public interface INetWasmExecutionRequestValidator
{
    void Validate(NetWasmExecutionRequest request);
}
