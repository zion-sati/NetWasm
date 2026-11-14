// From the compiler harness's FileSystemWatcherCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
using System.IO;
namespace FileSystemWatcherFixture;

sealed class Probe : FileSystemWatcher
{
    public void RaiseChanged(FileSystemEventArgs e) => OnChanged(e);
    public void RaiseCreated(FileSystemEventArgs e) => OnCreated(e);
    public void RaiseRenamed(RenamedEventArgs e) => OnRenamed(e);
    public void RaiseError(ErrorEventArgs e) => OnError(e);
}

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; }
        catch (T) { return true; }
    }

#if !DESKTOP_ORACLE
    static bool Unsupported(Action action)
    {
        try { action(); return false; }
        catch (PlatformNotSupportedException ex) { return ex.Message == "System.IO.FileSystem.Watcher is not supported on this platform."; }
    }
#endif

    public static int Run(int seed)
    {
        var watcher = new Probe();
        if (watcher.Path != "" || watcher.Filter != "*" || watcher.Filters.Count != 0 || watcher.IncludeSubdirectories) return 1;
        if (watcher.EnableRaisingEvents || watcher.SynchronizingObject != null || watcher.Site != null) return 2;
        if (watcher.NotifyFilter != (NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName) || watcher.InternalBufferSize != 8192) return 3;

        // Filters: Filter is the first entry, "*" when there is none; "*.*" and empty mean everything.
        watcher.Filter = "*.txt";
        if (watcher.Filters.Count != 1 || watcher.Filters[0] != "*.txt") return 4;
        watcher.Filters.Add("*.md");
        if (watcher.Filter != "*.txt" || watcher.Filters.Count != 2 || !watcher.Filters.Contains("*.md")) return 5;
        watcher.Filter = "*.*";
        if (watcher.Filters.Count != 1 || watcher.Filter != "*") return 6;
        watcher.Filter = "";
        if (watcher.Filter != "*") return 7;
        watcher.Filters.Clear();
        if (watcher.Filter != "*" || watcher.Filters.Count != 0) return 8;

        // Validation is upstream's.
        if (!Throws<ArgumentException>(() => watcher.NotifyFilter = (NotifyFilters)0x1000)) return 9;
        watcher.NotifyFilter = NotifyFilters.Size | NotifyFilters.Attributes;
        if (watcher.NotifyFilter != (NotifyFilters.Size | NotifyFilters.Attributes)) return 10;
        watcher.InternalBufferSize = 100;
        if (watcher.InternalBufferSize != 4096) return 11;
        watcher.InternalBufferSize = 65536;
        if (watcher.InternalBufferSize != 65536) return 12;
        watcher.IncludeSubdirectories = true;
        if (!watcher.IncludeSubdirectories) return 13;
        if (!Throws<ArgumentNullException>(() => new FileSystemWatcher(null!))) return 14;
        if (!Throws<ArgumentException>(() => new FileSystemWatcher(""))) return 15;
        if (!Throws<ArgumentException>(() => new FileSystemWatcher("/netwasm/no/such/directory"))) return 16;
        if (!Throws<ArgumentException>(() => watcher.Path = "/netwasm/no/such/directory") || watcher.Path != "") return 17;

        // The events and their raisers.
        int changed = 0, created = 0, renamed = 0, errors = 0;
        FileSystemEventHandler onChanged = (sender, e) => { if (sender == watcher && e.ChangeType == WatcherChangeTypes.Changed && e.Name == "a.txt") changed++; };
        watcher.Changed += onChanged;
        watcher.Created += (sender, e) => { if (e.ChangeType == WatcherChangeTypes.Created) created++; };
        watcher.Renamed += (sender, e) => { if (e.OldName == "old.txt" && e.Name == "new.txt") renamed++; };
        watcher.Error += (sender, e) => { if (e.GetException().Message == "lost") errors++; };
        watcher.RaiseChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, "/d", "a.txt"));
        watcher.RaiseCreated(new FileSystemEventArgs(WatcherChangeTypes.Created, "/d", "b.txt"));
        watcher.RaiseRenamed(new RenamedEventArgs(WatcherChangeTypes.Renamed, "/d", "new.txt", "old.txt"));
        watcher.RaiseError(new ErrorEventArgs(new InternalBufferOverflowException("lost")));
        watcher.Changed -= onChanged;
        watcher.RaiseChanged(new FileSystemEventArgs(WatcherChangeTypes.Changed, "/d", "a.txt"));
        if (changed != 1 || created != 1 || renamed != 1 || errors != 1) return 18;

        // ISupportInitialize defers the start to EndInit, which starts only a watcher with a path.
        watcher.BeginInit();
        watcher.EnableRaisingEvents = true;
        if (!watcher.EnableRaisingEvents) return 19;
        watcher.EndInit();
        watcher.EnableRaisingEvents = false;
        if (watcher.EnableRaisingEvents) return 20;

#if !DESKTOP_ORACLE
        // Starting a watch is what WASI 0.3 can't do.
        if (!Unsupported(() => watcher.EnableRaisingEvents = true) || watcher.EnableRaisingEvents) return 21;
        if (!Unsupported(() => watcher.WaitForChanged(WatcherChangeTypes.All, 0))) return 22;
        if (!Unsupported(() => watcher.WaitForChanged(WatcherChangeTypes.Renamed))) return 23;
        // WaitForChanged unregisters its handlers on the way out.
        watcher.RaiseRenamed(new RenamedEventArgs(WatcherChangeTypes.Renamed, "/d", "new.txt", "old.txt"));
        if (renamed != 2) return 24;
#endif

        watcher.Dispose();
        if (!Throws<ObjectDisposedException>(() => watcher.EnableRaisingEvents = true)) return 25;
        watcher.Dispose();
        return seed + 1;
    }
}
