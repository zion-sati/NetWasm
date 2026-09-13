using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

public interface IImplementedInterfaceResolver
{
    ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type);
}
