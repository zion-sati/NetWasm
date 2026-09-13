using System;

namespace NetWasm.Hosting.Execution;

public interface INetWasmExecutionRequestReader
{
    NetWasmExecutionRequest Read(ReadOnlyMemory<byte> utf8Json);
}
