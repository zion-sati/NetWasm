namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusRunDirectoryCleanerTests
{
    [Fact]
    public void RemovesOnlyTheCompletedRootAndDoesNotFollowNestedLinks()
    {
        var factory = Assert.IsAssignableFrom<ICorpusRunDirectoryFactory>(new CorpusRunDirectoryFactory());
        var root = factory.Create();
        var sibling = factory.Create();
        var cleaner = Assert.IsAssignableFrom<ICorpusRunDirectoryCleaner>(new CorpusRunDirectoryCleaner());
        try
        {
            File.WriteAllText(Path.Combine(sibling, "retained.txt"), "retained");
            Directory.CreateDirectory(Path.Combine(root, "Debug"));
            File.WriteAllText(Path.Combine(root, "Debug", "fixture.cs"), "disposable");
            Directory.CreateSymbolicLink(Path.Combine(root, "linked"), sibling);

            cleaner.Clean(root);

            Assert.False(Directory.Exists(root));
            Assert.Equal("retained", File.ReadAllText(Path.Combine(sibling, "retained.txt")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            Directory.Delete(sibling, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void RefusesUnscopedPathsBeforeDeletingAnything(int invalid)
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-cleanup-negative-");
        var cleaner = Assert.IsAssignableFrom<ICorpusRunDirectoryCleaner>(new CorpusRunDirectoryCleaner());
        try
        {
            var candidate = invalid switch
            {
                0 => null!,
                1 => " ",
                2 => "netwasm-corpus-relative",
                3 => Path.GetTempPath(),
                4 => directory.FullName,
                5 => Path.Combine(Path.GetTempPath(), "netwasm-corpus-"),
                _ => Path.Combine(directory.FullName, "netwasm-corpus-nested"),
            };

            Assert.ThrowsAny<ArgumentException>(() => cleaner.Clean(candidate));

            Assert.True(directory.Exists);
            Assert.Empty(directory.EnumerateFileSystemInfos());
        }
        finally { directory.Delete(); }
    }

    [Fact]
    public void MissingRootFailsLoudly()
    {
        var factory = Assert.IsAssignableFrom<ICorpusRunDirectoryFactory>(new CorpusRunDirectoryFactory());
        var root = factory.Create();
        Directory.Delete(root);
        var cleaner = Assert.IsAssignableFrom<ICorpusRunDirectoryCleaner>(new CorpusRunDirectoryCleaner());

        Assert.Throws<DirectoryNotFoundException>(() => cleaner.Clean(root));
    }

    [Fact]
    public void LinkedRootCannotAuthorizeDeletionOfItsTarget()
    {
        var factory = Assert.IsAssignableFrom<ICorpusRunDirectoryFactory>(new CorpusRunDirectoryFactory());
        var root = factory.Create();
        var target = factory.Create();
        var cleaner = Assert.IsAssignableFrom<ICorpusRunDirectoryCleaner>(new CorpusRunDirectoryCleaner());
        try
        {
            Directory.Delete(root);
            Directory.CreateSymbolicLink(root, target);
            File.WriteAllText(Path.Combine(target, "retained.txt"), "retained");

            Assert.Throws<IOException>(() => cleaner.Clean(root));

            Assert.Equal("retained", File.ReadAllText(Path.Combine(target, "retained.txt")));
        }
        finally
        {
            Directory.Delete(root);
            Directory.Delete(target, recursive: true);
        }
    }
}

internal sealed class RecordingCorpusCleanup : ICorpusRunDirectoryCleaner
{
    public List<string> Directories { get; } = [];
    public Exception? Failure { get; init; }
    public Action<string>? BeforeClean { get; init; }

    public void Clean(string directory)
    {
        BeforeClean?.Invoke(directory);
        Directories.Add(directory);
        if (Failure is not null) throw Failure;
    }
}
