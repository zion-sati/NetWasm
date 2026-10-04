using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface ILinkedRuntimeModuleReader
{
    RuntimeLinkedModule Read(ReadOnlyMemory<byte> module);
}
