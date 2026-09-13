namespace NetWasm.Hosting.Execution;

internal interface IExecutionContractRegistry
{
    IExecutionContractDefinition Resolve(string key);
}
