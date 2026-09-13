namespace NetWasm.Hosting.Execution;

public interface INetWasmExecutionResultValidator
{
    void Validate(NetWasmExecutionResult result);
}
