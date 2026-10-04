using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface ILinkerSymbolTraceReader
{
    ImmutableArray<RuntimeLinkerSymbolEvent> Read(string trace);
}
