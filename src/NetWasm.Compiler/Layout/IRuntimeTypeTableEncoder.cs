using System.Collections.Immutable;

namespace NetWasm.Compiler.Layout;

internal interface IRuntimeTypeTableEncoder
{
    ImmutableArray<byte> Encode(RuntimeTypeTableRequest request);
}
