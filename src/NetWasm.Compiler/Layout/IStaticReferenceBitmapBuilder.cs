using System.Collections.Immutable;

namespace NetWasm.Compiler.Layout;

internal interface IStaticReferenceBitmapBuilder
{
    byte[] Build(ImmutableArray<int> referenceOffsets, int bitCount, int referenceWordSize);
}
