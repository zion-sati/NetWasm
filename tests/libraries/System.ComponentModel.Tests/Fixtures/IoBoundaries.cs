// From the compiler harness's IoBoundariesCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.IO.IsolatedStorage;
using System.IO.Pipes;
using System.Text;
namespace IoBoundariesFixture;

sealed class Store : IsolatedStorage
{
    public override void Remove() { }
    public char External => SeparatorExternal;
    public char Internal => SeparatorInternal;
    public void Init(IsolatedStorageScope scope) => InitStore(scope, null, null);
}

sealed class Normalized : INormalizeForIsolatedStorage
{
    public object Normalize() => "normalized";
}

sealed class Loop : PipeStream
{
    public Loop(PipeDirection direction) : base(direction, 0) { }
    public Loop(PipeDirection direction, PipeTransmissionMode mode) : base(direction, mode, 16) { }
    public void Connect() => IsConnected = true;
    public bool Exposed => IsHandleExposed;
}

sealed class OneByte : PipeStream
{
    public OneByte() : base(PipeDirection.InOut, 0) { IsConnected = true; }
    public int Written = -1;
    public override int Read(Span<byte> buffer) { buffer[0] = 7; return 1; }
    public override void Write(ReadOnlySpan<byte> buffer) { Written = buffer[0]; }
}

public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    static bool Unsupported(Action action)
    {
#if DESKTOP_ORACLE
        return true;
#else
        try { action(); return false; }
        catch (PlatformNotSupportedException e) { return e.Message.Length > 0; }
#endif
    }

    public static int Run(int input)
    {
        // File-system event data combines paths without touching the file system.
        var created = new FileSystemEventArgs(WatcherChangeTypes.Created, "/dir", "a.txt");
        if (created.FullPath != "/dir/a.txt" || created.Name != "a.txt" || created.ChangeType != WatcherChangeTypes.Created) return 1;
        if (new FileSystemEventArgs(WatcherChangeTypes.Changed, "/dir/", null).FullPath != "/dir/" || !Throws<ArgumentNullException>(() => new FileSystemEventArgs(0, null!, "x"))) return 2;
        var renamed = new RenamedEventArgs(WatcherChangeTypes.Renamed, "/d", "new", "old");
        if (renamed.OldFullPath != "/d/old" || renamed.OldName != "old" || renamed.FullPath != "/d/new") return 3;
        var failure = new InvalidOperationException("x");
        if (!ReferenceEquals(new ErrorEventArgs(failure).GetException(), failure)) return 4;
        var result = new WaitForChangedResult { ChangeType = WatcherChangeTypes.Deleted, Name = "n", OldName = "o", TimedOut = true };
        if (result.ChangeType != WatcherChangeTypes.Deleted || result.Name != "n" || result.OldName != "o" || !result.TimedOut) return 5;
        string? seen = null;
        FileSystemEventHandler onEvent = (_, e) => seen = e.Name;
        RenamedEventHandler onRename = (_, e) => seen += e.OldName;
        ErrorEventHandler onError = (_, e) => seen += e.GetException().Message;
        onEvent(null!, created); onRename(null!, renamed); onError(null!, new ErrorEventArgs(failure));
        if (seen != "a.txtoldx") return 6;
        if ((int)NotifyFilters.Security != 256 || (int)NotifyFilters.CreationTime != 64 || (int)WatcherChangeTypes.All != 15) return 7;
        if (new InternalBufferOverflowException().HResult != unchecked((int)0x80131905) || new DriveNotFoundException().HResult != unchecked((int)0x80070003)) return 8;
        if (new DriveNotFoundException(null).Message.Length == 0 || new InternalBufferOverflowException("m").Message != "m") return 9;

        // A watcher can be constructed and disposed without monitoring anything.
        using (var watcher = new FileSystemWatcher())
        {
            ((ISupportInitialize)watcher).BeginInit();
            ((ISupportInitialize)watcher).EndInit();
        }

        // Isolated storage's abstract base keeps upstream's guards.
        var store = new Store();
        if (store.Scope != IsolatedStorageScope.None || store.Internal != '.' || store.External != Path.DirectorySeparatorChar || store.IncreaseQuotaTo(10)) return 10;
        if (!Throws<InvalidOperationException>(() => _ = store.Quota) || !Throws<InvalidOperationException>(() => _ = store.AssemblyIdentity) || !Throws<InvalidOperationException>(() => _ = store.UsedSize)) return 11;
        if (!Throws<ArgumentException>(() => store.Init(IsolatedStorageScope.None))) return 12;
        if (new IsolatedStorageException().HResult != unchecked((int)0x80131450) || (int)IsolatedStorageScope.Application != 32) return 13;
        if ((string)new Normalized().Normalize() != "normalized") return 14;
        // The stores themselves are upstream's Unix implementation (NetWasm.System.IO.IsolatedStorage.Tests).
        if (!IsolatedStorageFile.IsEnabled) return 15;

        // A PipeStream subclass validates arguments and keeps its connection state.
        if (!Throws<ArgumentOutOfRangeException>(() => new Loop((PipeDirection)0)) || !Throws<ArgumentOutOfRangeException>(() => new Loop(PipeDirection.In, (PipeTransmissionMode)5))) return 18;
        var loop = new Loop(PipeDirection.Out);
        if (loop.CanRead || !loop.CanWrite || loop.CanSeek || loop.IsConnected || loop.IsAsync || loop.Exposed) return 19;
        if (!Throws<NotSupportedException>(() => _ = loop.Length) || !Throws<NotSupportedException>(() => loop.Seek(0, SeekOrigin.Begin))) return 20;
        if (!Throws<InvalidOperationException>(() => loop.Flush()) || !Throws<InvalidOperationException>(() => _ = loop.IsMessageComplete)) return 21;
        if (!Throws<InvalidOperationException>(() => _ = loop.SafePipeHandle)) return 22;
        loop.Connect();
        loop.Flush();
        if (!loop.IsConnected) return 23;
        loop.Dispose();
        if (loop.IsConnected || !Throws<ObjectDisposedException>(() => loop.Flush())) return 24;
        var oneByte = new OneByte();
        oneByte.WriteByte(9);
        if (oneByte.ReadByte() != 7 || oneByte.Written != 9) return 25;
        if (NamedPipeServerStream.MaxAllowedServerInstances != -1 || !Throws<ArgumentException>(() => new NamedPipeServerStream(""))) return 26;
        if (!Throws<NotSupportedException>(() => new AnonymousPipeServerStream(PipeDirection.InOut))) return 27;
        if (!Unsupported(() => new AnonymousPipeServerStream(PipeDirection.Out))) return 28;
        if (!Unsupported(() => new NamedPipeServerStream("netwasm-pipe"))) return 29;
        if (!Unsupported(() => new NamedPipeClientStream(".", "netwasm-pipe"))) return 30;
        if (!Unsupported(() => new Loop(PipeDirection.In).ReadByte()) || !Unsupported(() => _ = new Loop(PipeDirection.In).TransmissionMode)) return 31;

        // ZIP and the codecs link NetWasm.App.Runtime's zlib archive, which this harness does not link; they are
        // qualified as an SDK consumer (NetWasm.System.IO.Compression.Tests, NetWasm.System.IO.Compression.ZipFile.Tests).
        if ((int)CompressionMode.Decompress != 0 || (int)CompressionMode.Compress != 1) return 48;
        return input + 1;
    }
}
