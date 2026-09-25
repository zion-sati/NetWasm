namespace NetWasm.Compiler.Tests.Correctness;

public sealed class CorpusRunDirectoryFactoryTests
{
    [Fact]
    public void CreateAllocatesDistinctEmptyDirectoriesAcrossFactoryInstances()
    {
        var firstFactory = Assert.IsAssignableFrom<ICorpusRunDirectoryFactory>(new CorpusRunDirectoryFactory());
        var secondFactory = Assert.IsAssignableFrom<ICorpusRunDirectoryFactory>(new CorpusRunDirectoryFactory());
        var roots = new List<string>();
        try
        {
            roots.Add(firstFactory.Create());
            roots.Add(firstFactory.Create());
            roots.Add(secondFactory.Create());

            Assert.Equal(roots.Count, roots.Distinct(StringComparer.Ordinal).Count());
            foreach (var root in roots)
            {
                Assert.True(Path.IsPathFullyQualified(root));
                Assert.True(Directory.Exists(root));
                Assert.Empty(Directory.EnumerateFileSystemEntries(root));
                Assert.StartsWith("netwasm-corpus-", Path.GetFileName(root), StringComparison.Ordinal);
            }
        }
        finally
        {
            foreach (var root in roots)
            {
                Directory.Delete(root);
            }
        }
    }
}
