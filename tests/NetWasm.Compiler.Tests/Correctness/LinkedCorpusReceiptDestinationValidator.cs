namespace NetWasm.Compiler.Tests.Correctness;

internal sealed record LinkedCorpusReceiptEnvironment(string? Directory);

internal interface ILinkedCorpusReceiptDestinationValidator
{
    string Validate(string compilationDirectory);
}

// Filesystem adapter: validates the durable destination before expensive linked execution.
internal sealed class LinkedCorpusReceiptDestinationValidator(
    LinkedCorpusReceiptEnvironment environment) : ILinkedCorpusReceiptDestinationValidator
{
    public string Validate(string compilationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(compilationDirectory);
        var destination = environment.Directory;
        if (string.IsNullOrWhiteSpace(destination) || !Path.IsPathFullyQualified(destination))
            throw new InvalidOperationException("NETWASM_CORPUS_RECEIPTS must be an absolute existing directory.");
        var root = new DirectoryInfo(destination);
        if (!root.Exists)
            throw new DirectoryNotFoundException("The linked receipt directory must already exist.");
        // Resolve directory links, including macOS /tmp, before checking ownership.
        var resolvedRoot = Resolve(root);
        var runRoot = Resolve(new DirectoryInfo(compilationDirectory).Parent!);
        var relative = Path.GetRelativePath(runRoot, resolvedRoot);
        if (relative == "." || (!Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            throw new ArgumentException("Linked receipts must be outside the disposable corpus root.");

        // Permission bits alone do not account for ACLs or read-only mounts. Create a
        // unique, automatically removed probe without touching existing evidence.
        var probe = Path.Combine(resolvedRoot, ".netwasm-receipt-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 1, FileOptions.DeleteOnClose);
            stream.WriteByte(0);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "NETWASM_CORPUS_RECEIPTS must allow creating and writing receipt files.", failure);
        }
        return resolvedRoot;
    }

    private static string Resolve(DirectoryInfo directory)
    {
        var target = directory.ResolveLinkTarget(returnFinalTarget: true);
        if (target is not null) return Resolve(new DirectoryInfo(target.FullName));
        return directory.Parent is { } parent
            ? Path.Combine(Resolve(parent), directory.Name)
            : directory.FullName;
    }
}
