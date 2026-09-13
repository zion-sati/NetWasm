namespace NetWasm.Hosting.Execution;

public interface IExecutionDescriptorWriter
{
    byte[] Write(ExecutionDescriptor descriptor);
}
