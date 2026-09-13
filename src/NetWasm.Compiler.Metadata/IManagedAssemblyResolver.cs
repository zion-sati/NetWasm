using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal interface IManagedAssemblyResolver
{
    ManagedAssembly Resolve(AssemblyIdentity identity);
}
