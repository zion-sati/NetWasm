using System.Collections.Immutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

public interface IManagedAssemblyLoader
{
    ManagedAssembly Load(
        string path,
        ImmutableDictionary<string, string> referenceAssemblyAliases,
        ImmutableDictionary<(AssemblyIdentity Assembly, string FullName), AssemblyIdentity>? typeForwarders = null,
        INativeImportBinder? nativeBinding = null);
}
