using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeLinkArgumentBuilder
{
    ImmutableArray<string> Build(RuntimeLinkRequest request);
}
