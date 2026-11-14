using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata;

public interface IMetadataCompilationLoader
{
    /// <summary>
    /// Loads executable definitions from the entry and implementation references.
    /// Metadata references contribute forwarding information only, including when
    /// their identity matches an implementation; their bodies never enter the lease.
    /// </summary>
    IMetadataCompilationLease Load(
        string entryAssemblyPath,
        IEnumerable<string> referencePaths,
        ImmutableDictionary<string, string>? referenceAssemblyAliases = null,
        IEnumerable<string>? metadataReferencePaths = null,
        IEnumerable<string>? originalContractReferencePaths = null,
        INativeImportBinder? nativeBinding = null);
}
