namespace NetWasm.Runtime.Pack.Materialization;

internal sealed record RuntimeLinkerSymbolEvent(
    string InputIdentity,
    RuntimeLinkerSymbolEventKind Kind,
    string EntryPoint);

internal enum RuntimeLinkerSymbolEventKind
{
    Reference,
    LazyDefinition,
    Definition,
}
