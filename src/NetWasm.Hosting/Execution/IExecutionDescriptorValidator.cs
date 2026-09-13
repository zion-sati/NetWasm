namespace NetWasm.Hosting.Execution;

public interface IExecutionDescriptorValidator
{
    void Validate(ExecutionDescriptor descriptor);
}
