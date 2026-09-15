using NetWasm.Compiler.Metadata.ManagedExecutables;

namespace NetWasm.Compiler.Tasks.Compilation;

internal sealed class ManagedEntryPointReader : IManagedEntryPointReader
{
    private readonly IManagedExecutableEntryPointSelector _entries;

    public ManagedEntryPointReader() : this(new ManagedExecutableEntryPointSelector()) { }

    public ManagedEntryPointReader(IManagedExecutableEntryPointSelector entries) =>
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));

    public ManagedEntryPoint Read(string assemblyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyPath);
        var entry = _entries.SelectEntryPoint(assemblyPath, File.ReadAllBytes(assemblyPath));
        return new(entry.TypeName, entry.MethodName, entry.MetadataToken, entry.Abi);
    }
}
