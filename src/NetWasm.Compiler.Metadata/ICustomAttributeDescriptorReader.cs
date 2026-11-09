using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface ICustomAttributeDescriptorReader
{
    ImmutableArray<CustomAttributeDescriptor> Read(CliTypeIdentity type);
}

// The attributes an assembly declares on itself ([assembly: ...]).
public interface IAssemblyAttributeDescriptorReader
{
    ImmutableArray<CustomAttributeDescriptor> Read(AssemblyIdentity assembly);
}
