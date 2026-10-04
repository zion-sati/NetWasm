using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ICustomAttributeDescriptorReader
{
    ImmutableArray<CustomAttributeDescriptor> Read(CliTypeIdentity type);
}
