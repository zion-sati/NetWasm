using System;

namespace NetWasm.Compiler.Metadata.ManagedExecutables;

public interface IManagedExecutableEntryPointSelector
{
    /// <summary>
    /// Reads an executable entry point from virtual PE bytes. Without an explicit
    /// token, resolves the original async Main behind the CLR entry wrapper.
    /// An explicit token describes that method's actual ABI without unwrapping it.
    /// The assembly path is used only in diagnostics and is never opened.
    /// </summary>
    ManagedExecutableEntryPointSelection SelectEntryPoint(
        string assemblyPath, ReadOnlyMemory<byte> image, int? selectedMethodToken = null);
}
