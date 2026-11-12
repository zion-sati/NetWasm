using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Layout;

internal interface IAssignableTypeMetadataBuilder
{
    // The runtime type IDs a cast to the candidate accepts, ascending.
    ImmutableArray<int> Build(CliTypeIdentity candidate);
}
