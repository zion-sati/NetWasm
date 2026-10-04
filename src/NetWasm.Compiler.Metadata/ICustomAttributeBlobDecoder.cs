using System.Collections.Immutable;
using System.Reflection.Metadata;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface ICustomAttributeBlobDecoder
{
    CustomAttributeValue<CliTypeIdentity> Decode(BlobReader value, ImmutableArray<CliTypeIdentity> parameters);
}
