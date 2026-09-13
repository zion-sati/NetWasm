using System;

namespace NetWasm.Hosting.Execution;

public interface IExecutionDescriptorReader
{
    ExecutionDescriptor Read(ReadOnlyMemory<byte> utf8Json);
}
