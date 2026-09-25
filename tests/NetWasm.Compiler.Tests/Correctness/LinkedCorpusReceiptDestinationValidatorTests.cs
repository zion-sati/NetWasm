namespace NetWasm.Compiler.Tests.Correctness;

// These are filesystem adapter contracts; no compiler or linked toolchain is invoked.
public sealed class LinkedCorpusReceiptDestinationValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative")]
    public void RejectsMissingOrRelativeConfiguration(string? destination)
    {
        var validator = Create(destination);
        var failure = Assert.Throws<InvalidOperationException>(() => validator.Validate("compilation"));
        Assert.Contains("NETWASM_CORPUS_RECEIPTS", failure.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingCompilationDirectory(string? directory)
    {
        var validator = Create(null);
        Assert.ThrowsAny<ArgumentException>(() => validator.Validate(directory!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsMissingDirectoryOrExistingFileWithoutCreatingDestination(bool existingFile)
    {
        using var assets = new Assets();
        var path = Path.Combine(assets.Root, "destination");
        if (existingFile) File.WriteAllText(path, "preserve");
        Assert.Throws<DirectoryNotFoundException>(() => Create(path).Validate(assets.Compilation));
        Assert.False(Directory.Exists(path));
        if (existingFile) Assert.Equal("preserve", File.ReadAllText(path));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("compilation")]
    [InlineData("link")]
    [InlineData("ancestor-link")]
    public void RejectsDisposableDestinationIncludingDirectoryLinks(string kind)
    {
        using var assets = new Assets();
        var destination = kind == "run" ? assets.Run : assets.Compilation;
        if (kind is "link" or "ancestor-link")
        {
            var link = Path.Combine(assets.Root, "link");
            Directory.CreateSymbolicLink(link, assets.Run);
            destination = kind == "link" ? link : Path.Combine(link, "Release");
        }
        Assert.Throws<ArgumentException>(() => Create(destination).Validate(assets.Compilation));
        Assert.Empty(Directory.EnumerateFileSystemEntries(assets.Compilation));
    }

    [Theory]
    [InlineData("sibling")]
    [InlineData("parent")]
    [InlineData("prefix")]
    [InlineData("link")]
    public void AcceptsDurableDestinationAndRemovesWriteProbe(string kind)
    {
        using var assets = new Assets();
        var destination = kind switch
        {
            "parent" => assets.Root,
            "prefix" => Directory.CreateDirectory(assets.Run + "-receipts").FullName,
            _ => Directory.CreateDirectory(Path.Combine(assets.Root, "receipts")).FullName,
        };
        var sentinel = Path.Combine(destination, "existing-receipt.json");
        File.WriteAllText(sentinel, "preserve");
        var configured = destination;
        if (kind == "link")
        {
            configured = Path.Combine(assets.Root, "link");
            Directory.CreateSymbolicLink(configured, destination);
        }
        var before = Directory.EnumerateFileSystemEntries(destination).Order().ToArray();
        var resolved = Create(configured).Validate(assets.Compilation);
        Assert.Equal("preserve", File.ReadAllText(Path.Combine(resolved, "existing-receipt.json")));
        Assert.Equal(before, Directory.EnumerateFileSystemEntries(destination).Order());
    }

    [Fact]
    public void RejectsUnwritableDirectoryWithConfigurationContext()
    {
        // Permission bits cannot deny the superuser, and Windows uses a different ACL model.
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        using var assets = new Assets();
        var destination = Directory.CreateDirectory(Path.Combine(assets.Root, "receipts")).FullName;
        var mode = File.GetUnixFileMode(destination);
        try
        {
            File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserExecute);
            var failure = Assert.Throws<InvalidOperationException>(() =>
                Create(destination).Validate(assets.Compilation));
            Assert.Contains("NETWASM_CORPUS_RECEIPTS", failure.Message);
            Assert.IsType<UnauthorizedAccessException>(failure.InnerException);
            Assert.Empty(Directory.EnumerateFileSystemEntries(destination));
        }
        finally
        {
            File.SetUnixFileMode(destination, mode);
        }
    }

    private static ILinkedCorpusReceiptDestinationValidator Create(string? destination) =>
        Assert.IsAssignableFrom<ILinkedCorpusReceiptDestinationValidator>(
            new LinkedCorpusReceiptDestinationValidator(new(destination)));

    private sealed class Assets : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("netwasm-receipt-preflight-").FullName;
        public string Run => Path.Combine(Root, "run");
        public string Compilation { get; }

        public Assets() => Compilation = Directory.CreateDirectory(Path.Combine(Run, "Release")).FullName;

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
