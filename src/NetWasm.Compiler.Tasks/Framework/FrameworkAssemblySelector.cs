namespace NetWasm.Compiler.Tasks.Framework;

// Walks assembly references from the program's assemblies through the framework's, taking each framework
// assembly the walk reaches. Where the program has its own copy of a framework assembly (a package that
// pruning kept), the higher version wins and a tie goes to the framework, as the .NET SDK resolves a package's
// conflict with the shared framework; C# made the same choice between the reference assemblies. A reference to
// a metadata facade continues through the facade's own references: a package built for netstandard names only
// netstandard, whose forwarders lead to CoreLib and the libraries. The result is in name order, so the
// compiler's inputs are stable.
internal sealed class FrameworkAssemblySelector(IAssemblyMetadataReader metadata) : IFrameworkAssemblySelector
{
    private readonly IAssemblyMetadataReader _metadata =
        metadata ?? throw new ArgumentNullException(nameof(metadata));

    public FrameworkAssemblySelection Select(
        IReadOnlyList<string> programAssemblies, IReadOnlyList<string> frameworkAssemblies, IReadOnlyList<string> facades)
    {
        ArgumentNullException.ThrowIfNull(programAssemblies);
        ArgumentNullException.ThrowIfNull(frameworkAssemblies);
        ArgumentNullException.ThrowIfNull(facades);
        var facadeByName = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in facades)
        {
            facadeByName.TryAdd(Path.GetFileNameWithoutExtension(path), path);
        }
        var framework = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in frameworkAssemblies)
        {
            if (!framework.TryAdd(Path.GetFileNameWithoutExtension(path), path))
            {
                throw new ArgumentException($"The framework has more than one '{Path.GetFileName(path)}'.", nameof(frameworkAssemblies));
            }
        }
        var selected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var displaced = new List<string>();
        var present = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        foreach (var path in programAssemblies)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            present.Add(name);
            if (framework.TryGetValue(name, out var frameworkPath) &&
                _metadata.Read(frameworkPath) is { } frameworkCopy &&
                (_metadata.Read(path)?.Version ?? new Version()) <= frameworkCopy.Version)
            {
                displaced.Add(path);
                selected[name] = frameworkPath;
                pending.Enqueue(frameworkPath);
                continue;
            }
            pending.Enqueue(path);
        }
        while (pending.TryDequeue(out var path))
        {
            foreach (var name in _metadata.Read(path)?.References ?? [])
            {
                if (framework.TryGetValue(name, out var reached) && present.Add(name))
                {
                    selected.Add(name, reached);
                    pending.Enqueue(reached);
                }
                else if (facadeByName.TryGetValue(name, out var facade) && present.Add(name))
                {
                    pending.Enqueue(facade);
                }
            }
        }
        return new([.. selected.Values], displaced);
    }
}
