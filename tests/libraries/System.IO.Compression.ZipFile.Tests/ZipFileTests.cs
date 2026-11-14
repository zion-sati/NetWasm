using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.IO.Compression.ZipFile.Tests;

// ZipFile reads and writes archives by path over the file system (WASI 0.3 on NetWasm; test hosts grant a private
// /tmp). The entries are stored: Deflate arrives with the compression codecs.
public sealed class ZipFileTests
{
    private static string NewDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "netwasm-zip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string Tree(string root)
    {
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(Path.Combine(source, "sub"));
        Directory.CreateDirectory(Path.Combine(source, "empty"));
        File.WriteAllText(Path.Combine(source, "a.txt"), "alpha");
        File.WriteAllBytes(Path.Combine(source, "sub", "b.bin"), [1, 2, 3, 4]);
        return source;
    }

    private static string Snapshot(string directory) => string.Join("|",
        Directory.GetFileSystemEntries(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/') +
                (File.Exists(path) ? "=" + Convert.ToHexString(File.ReadAllBytes(path)) : "/"))
            .Order(StringComparer.Ordinal));

    [Test]
    public async Task DirectoriesRoundTripThroughStoredArchives()
    {
        var root = NewDirectory();
        try
        {
            var source = Tree(root);
            var archive = Path.Combine(root, "tree.zip");
            global::System.IO.Compression.ZipFile.CreateFromDirectory(source, archive, CompressionLevel.NoCompression, includeBaseDirectory: false);

            string names;
            int fileType;
            int directoryType;
            using (var zip = global::System.IO.Compression.ZipFile.OpenRead(archive))
            {
                names = string.Join("|", zip.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal));
                fileType = (zip.GetEntry("a.txt")!.ExternalAttributes >> 16) & 0xF000;
                directoryType = (zip.GetEntry("empty/")!.ExternalAttributes >> 16) & 0xF000;
            }

            var target = Path.Combine(root, "target");
            global::System.IO.Compression.ZipFile.ExtractToDirectory(archive, target);

            await Assert.That(names).IsEqualTo("a.txt|empty/|sub/b.bin");
            await Assert.That(fileType).IsEqualTo(0x8000);
            await Assert.That(directoryType).IsEqualTo(0x4000);
            await Assert.That(Snapshot(target)).IsEqualTo(Snapshot(source));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task ArchivesOpenByPathForCreateUpdateAndExtract()
    {
        var root = NewDirectory();
        try
        {
            var file = Path.Combine(root, "note.txt");
            File.WriteAllText(file, "first");
            var archive = Path.Combine(root, "notes.zip");
            using (var zip = global::System.IO.Compression.ZipFile.Open(archive, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(file, "notes/one.txt", CompressionLevel.NoCompression);
            }

            using (var zip = global::System.IO.Compression.ZipFile.Open(archive, ZipArchiveMode.Update))
            {
                var entry = zip.CreateEntry("notes/two.txt", CompressionLevel.NoCompression);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write("second");
            }

            var extracted = Path.Combine(root, "one.txt");
            var refused = false;
            string two;
            using (var zip = global::System.IO.Compression.ZipFile.OpenRead(archive))
            {
                zip.GetEntry("notes/one.txt")!.ExtractToFile(extracted);
                try
                {
                    zip.GetEntry("notes/one.txt")!.ExtractToFile(extracted, overwrite: false);
                }
                catch (IOException)
                {
                    refused = true;
                }
                using var reader = new StreamReader(zip.GetEntry("notes/two.txt")!.Open());
                two = reader.ReadToEnd();
            }

            await Assert.That(File.ReadAllText(extracted)).IsEqualTo("first");
            await Assert.That(refused).IsTrue();
            await Assert.That(two).IsEqualTo("second");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public async Task DirectoriesRoundTripAsynchronously()
    {
        var root = NewDirectory();
        try
        {
            var source = Tree(root);
            var archive = Path.Combine(root, "tree.zip");
            await global::System.IO.Compression.ZipFile.CreateFromDirectoryAsync(source, archive, CompressionLevel.NoCompression, includeBaseDirectory: true);
            var target = Path.Combine(root, "target");
            await global::System.IO.Compression.ZipFile.ExtractToDirectoryAsync(archive, target);

            await Assert.That(Snapshot(Path.Combine(target, "source"))).IsEqualTo(Snapshot(source));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
