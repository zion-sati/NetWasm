// From the compiler harness's ResourcesAndEnumerationCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.IO.Enumeration;
using System.Resources;
namespace ResourcesAndEnumerationFixture;

sealed class StreamResourceManager : ResourceManager
{
    private readonly ResourceSet _set;
    public int Lookups;

    public StreamResourceManager(byte[] data) => _set = new ResourceSet(new MemoryStream(data));

    protected override ResourceSet? InternalGetResourceSet(CultureInfo culture, bool createIfNotExists, bool tryParents)
    {
        Lookups++;
        return _set;
    }
}

sealed class EmptyResourceManager : ResourceManager
{
}

sealed class PairReader : IResourceReader
{
    public bool Closed;
    public IDictionaryEnumerator GetEnumerator() => new Hashtable { ["Greeting"] = "hello", ["Answer"] = 42 }.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Close() => Closed = true;
    public void Dispose() => Close();
}

sealed class NamesEnumerator : FileSystemEnumerator<string>
{
    public NamesEnumerator(string directory) : base(directory) { }
    protected override string TransformEntry(ref FileSystemEntry entry) => entry.FileName.ToString();
}

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    static byte[] Generate(bool withCustom)
    {
        var output = new MemoryStream();
        var writer = new ResourceWriter(output);
        writer.AddResource("Greeting", "hello");
        writer.AddResource("Answer", 42);
        writer.AddResource("Flag", true);
        writer.AddResource("Price", 12.5m);
        writer.AddResource("When", new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc));
        writer.AddResource("Bytes", new byte[] { 1, 2, 3 });
        writer.AddResource("Blob", new MemoryStream(new byte[] { 9, 8, 7, 6 }));
        writer.AddResource("Nothing", (object?)null);
        if (withCustom) writer.AddResourceData("Custom", "Contoso.Widget, Contoso", new byte[] { 0, 1 });
        writer.Generate();
        byte[] data = output.ToArray();
        writer.Dispose();
        return data;
    }

    public static int Run(int input)
    {
        // ResourceWriter and ResourceReader round-trip primitive data through a stream.
        byte[] data = Generate(withCustom: false);
        byte[] customData = Generate(withCustom: true);
        if (BitConverter.ToInt32(data, 0) != ResourceManager.MagicNumber || ResourceManager.HeaderVersionNumber != 1) return 1;
        int count = 0;
        using (var reader = new ResourceReader(new MemoryStream(customData)))
        {
            IDictionaryEnumerator entries = reader.GetEnumerator();
            while (entries.MoveNext())
            {
                string key = (string)entries.Key;
                count++;
                if (key == "Custom") continue;
                object? value = entries.Value;
                bool ok = key switch
                {
                    "Greeting" => value is string s && s == "hello",
                    "Answer" => value is int i && i == 42,
                    "Flag" => value is bool b && b,
                    "Price" => value is decimal d && d == 12.5m,
                    "When" => value is DateTime t && t.Ticks == new DateTime(2026, 10, 20).Ticks && t.Kind == DateTimeKind.Utc,
                    "Bytes" => value is byte[] a && a.Length == 3 && a[2] == 3,
                    "Blob" => value is UnmanagedMemoryStream u && u.Length == 4 && u.ReadByte() == 9,
                    "Nothing" => value is null,
                    _ => false,
                };
                if (!ok) return 2;
            }
            if (count != 9) return 3;
            reader.GetResourceData("Greeting", out string type, out byte[] raw);
            if (type != "ResourceTypeCode.String" || raw.Length != 6 || raw[0] != 5 || raw[1] != (byte)'h') return 4;
            reader.GetResourceData("Custom", out string customType, out byte[] customRaw);
            if (customType != "Contoso.Widget, Contoso" || customRaw.Length != 2 || customRaw[1] != 1) return 5;
        }
        if (!Throws<ArgumentException>(() => new ResourceReader(new MemoryStream(new byte[] { 1, 2, 3, 4 })))) return 6;
        // Custom types deserialize through BinaryFormatter, which neither runtime enables here.
        if (!Throws<NotSupportedException>(() => new ResourceSet(new MemoryStream(customData)))) return 12;
        if (!Throws<ArgumentNullException>(() => new ResourceReader((Stream)null!)) || !Throws<ArgumentNullException>(() => new ResourceWriter((Stream)null!))) return 7;

        // ResourceSet lookups, case-insensitive fallback and custom data that needs deserialization.
        using (var set = new ResourceSet(new MemoryStream(data)))
        {
            if (set.GetString("Greeting") != "hello" || set.GetString("greeting") != null || set.GetString("greeting", true) != "hello") return 8;
            if (set.GetObject("Answer") is not int answer || answer != 42 || set.GetObject("Missing") != null) return 9;
            if (set.GetDefaultReader() != typeof(ResourceReader)) return 10;
#if DESKTOP_ORACLE
            if (set.GetDefaultWriter() != typeof(ResourceWriter)) return 10;
#else
            // As upstream, ResourceSet names ResourceWriter by its assembly-qualified name in
            // System.Resources.Writer. With the type-names sidecar (Debug) NetWasm resolves a name in the
            // assembly that defines the type and does not follow the facade's forwarder to CoreLib, so the name
            // does not load; without it (Release) lookup by name is unsupported.
            try
            {
                _ = set.GetDefaultWriter();
                return 10;
            }
            catch (TypeLoadException)
            {
            }
            catch (PlatformNotSupportedException)
            {
            }
#endif
            if (!Throws<InvalidOperationException>(() => set.GetString("Answer"))) return 11;
            int seen = 0;
            foreach (DictionaryEntry entry in set) seen++;
            if (seen != 8) return 13;
        }
        var pair = new PairReader();
        var custom = new ResourceSet(pair);
        if (custom.GetString("Greeting") != "hello" || (int)custom.GetObject("Answer")! != 42) return 14;
        custom.Close();
        if (!pair.Closed || !Throws<ObjectDisposedException>(() => custom.GetString("Greeting"))) return 15;

        // A ResourceManager subclass supplies its sets; lookup, case and streams follow upstream.
        var manager = new StreamResourceManager(data);
        if (manager.BaseName != "" || manager.IgnoreCase || manager.GetString("Greeting") != "hello") return 16;
        if (manager.GetString("greeting") != null) return 17;
        manager.IgnoreCase = true;
        if (manager.GetString("greeting", CultureInfo.InvariantCulture) != "hello" || manager.Lookups == 0) return 18;
        if (manager.GetObject("Answer") is not int managed || managed != 42 || manager.GetString("Missing") != null) return 19;
        UnmanagedMemoryStream? blob = manager.GetStream("Blob");
        if (blob is null || blob.Length != 4 || !Throws<InvalidOperationException>(() => manager.GetStream("Greeting"))) return 20;
        if (manager.GetResourceSet(CultureInfo.InvariantCulture, true, true) is not ResourceSet) return 21;
        if (!Throws<ArgumentNullException>(() => manager.GetString(null!))) return 22;
        // The protected constructor creates no set table, so the base lookup fails as upstream's does.
        if (!Throws<ArgumentNullException>(() => new EmptyResourceManager().GetString("Greeting"))) return 33;

        // File-system name matching is managed.
        if (!FileSystemName.MatchesSimpleExpression("*.txt", "notes.txt") || FileSystemName.MatchesSimpleExpression("*.txt", "notes.cs")) return 23;
        if (FileSystemName.MatchesSimpleExpression("*.TXT", "notes.txt", ignoreCase: false) || !FileSystemName.MatchesSimpleExpression("a?c", "abc")) return 24;
        if (FileSystemName.MatchesWin32Expression("*.*", "readme") || !FileSystemName.MatchesWin32Expression("*.*", "a.b") || !FileSystemName.MatchesWin32Expression("<.txt", "a.b.txt")) return 25;
        if (!FileSystemName.MatchesWin32Expression("*.txt", "A.TXT") || FileSystemName.MatchesWin32Expression("*.txt", "A.TXT", ignoreCase: false)) return 37;
        if (FileSystemName.TranslateWin32Expression("*.*") != "*" || FileSystemName.TranslateWin32Expression("a?.txt") != "a>.txt" || FileSystemName.TranslateWin32Expression("*.") != "<") return 26;
        if (FileSystemName.TranslateWin32Expression(null) != "*" || FileSystemName.TranslateWin32Expression("") != "*") return 27;

        // Find delegates run over an entry; a default entry has empty path parts.
        FileSystemEnumerable<string>.FindTransform transform = (ref FileSystemEntry entry) => entry.ToSpecifiedFullPath() + "|" + entry.IsDirectory;
        FileSystemEnumerable<string>.FindPredicate predicate = (ref FileSystemEntry entry) => entry.FileName.IsEmpty;
        FileSystemEntry blank = default;
        if (transform(ref blank) != "|False" || !predicate(ref blank)) return 28;
        if (blank.Directory.Length != 0 || blank.RootDirectory.Length != 0 || blank.OriginalRootDirectory.Length != 0) return 29;
        if (!Throws<ArgumentNullException>(() => new FileSystemEnumerable<string>(null!, transform)) ||
            !Throws<ArgumentNullException>(() => new FileSystemEnumerable<string>(".", null!)) ||
            !Throws<ArgumentNullException>(() => new NamesEnumerator(null!))) return 30;

#if !DESKTOP_ORACLE
        // NetWasm boundaries: assembly and file resources and entry metadata.
        if (!Throws<PlatformNotSupportedException>(() => new ResourceManager("Strings", typeof(object).Assembly))) return 31;
        if (!Throws<PlatformNotSupportedException>(() => ResourceManager.CreateFileBasedResourceManager("Strings", ".", null))) return 32;
        if (!Throws<PlatformNotSupportedException>(() => new ResourceReader("strings.resources")) ||
            !Throws<PlatformNotSupportedException>(() => new ResourceWriter("strings.resources")) ||
            !Throws<PlatformNotSupportedException>(() => new ResourceSet("strings.resources"))) return 34;
        // 0.12 has a file system, so directory enumeration works as upstream's does.
        foreach (string _ in new FileSystemEnumerable<string>(".", transform)) { }
        using (var names = new NamesEnumerator(".")) { while (names.MoveNext()) { } }
        // Entry metadata is upstream's Unix implementation over that file system.
        if (blank.ToFullPath() != "") return 36;
#endif
        return input + 1;
    }
}
