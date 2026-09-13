using System;

namespace NetWasm.Hosting.Execution;

public interface INetWasmExecutionResultReader
{
    NetWasmExecutionResult Read(ReadOnlyMemory<byte> utf8Json);
}
