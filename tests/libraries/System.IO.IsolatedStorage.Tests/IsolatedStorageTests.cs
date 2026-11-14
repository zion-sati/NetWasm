using System;
using System.IO;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.IO.IsolatedStorage.Tests;

// IsolatedStorage over the file system, as on Linux: an application's stores live under LocalApplicationData, and the
// entry assembly identifies them (on NetWasm by its name, as there is no executable path). The tests share the
// application's store, so they run one at a time.
[NotInParallel]
public sealed class IsolatedStorageTests
{
    [Test]
    public async Task TheApplicationStoreHoldsFilesAndDirectories()
    {
        using var store = IsolatedStorageFile.GetUserStoreForApplication();
        try
        {
            store.CreateDirectory("notes");
            using (var stream = store.OpenFile("notes/a.txt", FileMode.Create))
            using (var writer = new StreamWriter(stream))
            {
                writer.Write("hello");
            }

            string text;
            using (var reader = new StreamReader(store.OpenFile("notes/a.txt", FileMode.Open, FileAccess.Read)))
            {
                text = reader.ReadToEnd();
            }

            var exists = store.FileExists("notes/a.txt") && store.DirectoryExists("notes");
            var files = string.Join("|", store.GetFileNames("notes/*"));
            var directories = string.Join("|", store.GetDirectoryNames("*"));
            store.DeleteFile("notes/a.txt");
            store.DeleteDirectory("notes");

            await Assert.That(text).IsEqualTo("hello");
            await Assert.That(exists).IsTrue();
            await Assert.That(files).IsEqualTo("a.txt");
            await Assert.That(directories).IsEqualTo("notes");
            await Assert.That(store.FileExists("notes/a.txt") || store.DirectoryExists("notes")).IsFalse();
        }
        finally
        {
            store.Remove();
        }
    }

    [Test]
    public async Task TheApplicationIsIdentifiedConsistently()
    {
        using var first = IsolatedStorageFile.GetUserStoreForApplication();
        try
        {
            using (var stream = new IsolatedStorageFileStream("shared.txt", FileMode.Create, first))
            {
                stream.WriteByte(42);
            }

            using var second = IsolatedStorageFile.GetUserStoreForApplication();
            int value;
            using (var stream = new IsolatedStorageFileStream("shared.txt", FileMode.Open, FileAccess.Read, second))
            {
                value = stream.ReadByte();
            }

            await Assert.That(value).IsEqualTo(42);
            await Assert.That(second.GetFileNames().Contains("shared.txt")).IsTrue();
        }
        finally
        {
            first.Remove();
        }
    }
}
