// From the compiler harness's UnmanagedMemoryCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using TypeInfo = System.Reflection.TypeInfo;
namespace UnmanagedMemoryFixture;

struct Pair { public int A; public short B; }

sealed class Native : SafeBuffer
{
    public Native(int bytes) : base(true)
    {
        SetHandle(Marshal.AllocHGlobal(bytes));
        Initialize((ulong)bytes);
    }

    protected override bool ReleaseHandle()
    {
        Marshal.FreeHGlobal(handle);
        return true;
    }
}

sealed class Accessor : UnmanagedMemoryAccessor
{
    public Accessor(SafeBuffer buffer, long capacity, FileAccess access) : base(buffer, 0, capacity, access) { }
    public bool Open => IsOpen;
}

sealed class Context : ReflectionContext
{
    public override Assembly MapAssembly(Assembly assembly) => assembly;
    public override TypeInfo MapType(TypeInfo type) => type;
}

public static unsafe class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    static int Streams()
    {
        byte* memory = stackalloc byte[16];
        var stream = new UnmanagedMemoryStream(memory, 0, 16, FileAccess.ReadWrite);
        if (!stream.CanRead || !stream.CanWrite || !stream.CanSeek || stream.Length != 0 || stream.Capacity != 16) return 1;
        stream.Write(new byte[] { 1, 2, 3, 4 }, 0, 4);
        stream.WriteByte(5);
        stream.Write(new ReadOnlySpan<byte>(new byte[] { 6, 7 }));
        if (stream.Position != 7 || stream.Length != 7 || memory[6] != 7 || stream.PositionPointer != memory + 7) return 2;
        if (stream.Seek(-3, SeekOrigin.End) != 4 || stream.ReadByte() != 5) return 3;
        stream.Position = 0;
        var read = new byte[8];
        if (stream.Read(read, 0, 8) != 7 || read[3] != 4 || read[7] != 0 || stream.ReadByte() != -1) return 4;
        stream.Position = 1;
        Span<byte> span = stackalloc byte[2];
        if (stream.Read(span) != 2 || span[0] != 2 || span[1] != 3) return 5;
        stream.SetLength(3);
        if (stream.Length != 3 || stream.Position != 3) return 6;
        stream.PositionPointer = memory + 1;
        if (stream.Position != 1) return 7;
        if (!Throws<IOException>(() => stream.SetLength(17)) || !Throws<IOException>(() => stream.Seek(-1, SeekOrigin.Begin))) return 8;
        if (!Throws<ArgumentOutOfRangeException>(() => stream.Position = -1) || !Throws<ArgumentException>(() => stream.Seek(0, (SeekOrigin)9))) return 9;
        stream.Position = 15;
        if (!Throws<NotSupportedException>(() => stream.Write(new byte[2], 0, 2))) return 10;
        stream.WriteAsync(new byte[] { 9 }, 0, 1).GetAwaiter().GetResult();
        if (stream.Length != 16 || memory[15] != 9) return 11;
        stream.Dispose();
        if (stream.CanRead || !Throws<ObjectDisposedException>(() => stream.ReadByte())) return 12;

        var readOnly = new UnmanagedMemoryStream(memory, 4, 16, FileAccess.Read);
        if (readOnly.CanWrite || !Throws<NotSupportedException>(() => readOnly.WriteByte(1)) || readOnly.ReadByte() != 1) return 13;
        if (!Throws<ArgumentOutOfRangeException>(() => new UnmanagedMemoryStream(memory, 17, 16, FileAccess.Read))) return 14;
        if (!Throws<ArgumentNullException>(() => new UnmanagedMemoryStream((byte*)null, 0))) return 15;

        using var native = new Native(16);
        using var buffered = new UnmanagedMemoryStream(native, 4, 8, FileAccess.ReadWrite);
        buffered.Write(new byte[] { 10, 11 }, 0, 2);
        if (buffered.Capacity != 8 || buffered.Position != 2 || native.Read<byte>(5) != 11) return 16;
        if (!Throws<NotSupportedException>(() => _ = buffered.PositionPointer)) return 17;
        return 0;
    }

    static int Accessors()
    {
        using var native = new Native(32);
        using var accessor = new Accessor(native, 32, FileAccess.ReadWrite);
        if (!accessor.CanRead || !accessor.CanWrite || accessor.Capacity != 32 || !accessor.Open) return 20;
        accessor.Write(0, 0x01020304);
        accessor.Write(4, (short)-2);
        accessor.Write(8, 2.5d);
        accessor.Write(16, true);
        accessor.Write(17, 'x');
        if (accessor.ReadInt32(0) != 0x01020304 || accessor.ReadByte(0) != 4 || accessor.ReadInt16(4) != -2 || accessor.ReadDouble(8) != 2.5d) return 21;
        if (!accessor.ReadBoolean(16) || accessor.ReadChar(17) != 'x' || accessor.ReadUInt16(4) != 65534) return 22;
        var pair = new Pair { A = 7, B = 8 };
        accessor.Write(20, ref pair);
        accessor.Read(20, out Pair copy);
        if (copy.A != 7 || copy.B != 8) return 23;
        accessor.WriteArray(24, new short[] { 1, 2, 3 }, 0, 3);
        var shorts = new short[3];
        if (accessor.ReadArray(24, shorts, 0, 3) != 3 || shorts[2] != 3) return 24;
        accessor.Write(0, 12.5m);
        if (accessor.ReadDecimal(0) != 12.5m) return 29;
        if (!Throws<ArgumentException>(() => accessor.ReadInt32(30)) || !Throws<ArgumentOutOfRangeException>(() => accessor.ReadInt32(-1))) return 25;
        if (!Throws<ArgumentException>(() => accessor.ReadInt64(28))) return 26;

        using var readOnly = new Accessor(native, 32, FileAccess.Read);
        if (readOnly.CanWrite || !Throws<NotSupportedException>(() => readOnly.Write(0, 1))) return 27;
        accessor.Dispose();
        if (accessor.Open || !Throws<ObjectDisposedException>(() => accessor.ReadByte(0))) return 28;
        return 0;
    }

    static int Writers()
    {
        var nl = Environment.NewLine;
        var text = new StringWriter();
        var writer = new IndentedTextWriter(text, "  ");
        if (IndentedTextWriter.DefaultTabString != "    " || writer.InnerWriter != text || !writer.Encoding.Equals(text.Encoding)) return 30;
        writer.WriteLine("a");
        writer.Indent = 2;
        writer.WriteLine("b{0}", 1);
        writer.Write("c");
        writer.Write('d');
        writer.Write(5);
        writer.WriteLine();
        writer.WriteLineNoTabs("raw");
        writer.Indent = -3;
        writer.WriteLine(true);
        writer.WriteLineAsync("async").GetAwaiter().GetResult();
        writer.Flush();
        if (writer.Indent != 0 || text.ToString() != "a" + nl + "    b1" + nl + "    cd5" + nl + "raw" + nl + "True" + nl + "async" + nl) return 31;
        writer.NewLine = "|";
        writer.WriteLine("x");
        if (text.NewLine != "|" || !text.ToString().EndsWith("x|", StringComparison.Ordinal)) return 32;
        return 0;
    }

    static int Residuals()
    {
        if (Debugger.IsAttached || Debugger.IsLogging() || Debugger.DefaultCategory != null) return 40;
        Debugger.Log(0, "category", "message");
        Debugger.NotifyOfCrossThreadDependency();
#if !DESKTOP_ORACLE
        Debugger.Break();
        // No debugger to start: Launch reports false, as CoreCLR on Linux does when it cannot start one.
        if (Debugger.Launch()) return 41;
#endif

        int value = 5;
        int* address = &value;
        object boxed = Pointer.Box(address, typeof(int*));
        if (Pointer.Unbox(boxed) != address || !boxed.Equals(Pointer.Box(address, typeof(int*))) || boxed.GetHashCode() != Pointer.Box(address, typeof(int*)).GetHashCode()) return 43;
        if (!Throws<ArgumentException>(() => Pointer.Box(address, typeof(int))) || !Throws<ArgumentNullException>(() => Pointer.Box(address, null!))) return 44;
        if (!Throws<ArgumentException>(() => Pointer.Unbox("text")) || boxed.Equals("text")) return 45;

        if (!Throws<ArgumentOutOfRangeException>(() => new MemoryFailPoint(0))) return 46;
#if !DESKTOP_ORACLE
        // Upstream's MemoryFailPoint with its Unix half: a small reservation is granted.
        using (new MemoryFailPoint(1)) { }
#endif

        var context = new Context();
        if (context.MapAssembly(null!) != null || !Throws<ArgumentNullException>(() => context.GetTypeForObject(null!))) return 48;
        if (context.GetTypeForObject("text") != typeof(string)) return 49;

        var proxy = new AssemblyNameProxy();
        if (!Throws<ArgumentNullException>(() => proxy.GetAssemblyName(null!))) return 50;
#if !DESKTOP_ORACLE
        if (!Throws<PlatformNotSupportedException>(() => proxy.GetAssemblyName("missing.dll"))) return 51;
#endif
        return 0;
    }

    public static int Run(int input)
    {
        int failure = Streams();
        if (failure == 0) failure = Accessors();
        if (failure == 0) failure = Writers();
        if (failure == 0) failure = Residuals();
        return failure == 0 ? input + 1 : failure;
    }
}
