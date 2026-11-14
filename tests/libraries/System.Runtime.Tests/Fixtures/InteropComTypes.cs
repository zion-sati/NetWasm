// From the compiler harness's InteropComTypesCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
namespace ComTypesFixture;
sealed class MemoryStream : IStream
{
    private byte[] _data = new byte[0];
    private long _position;
    public int Writes;
    public void Read(byte[] pv, int cb, IntPtr pcbRead)
    {
        int count = (int)Math.Min(cb, _data.Length - _position);
        Array.Copy(_data, _position, pv, 0, count);
        _position += count;
    }
    public void Write(byte[] pv, int cb, IntPtr pcbWritten)
    {
        var grown = new byte[Math.Max(_data.Length, _position + cb)];
        Array.Copy(_data, grown, _data.Length);
        Array.Copy(pv, 0, grown, _position, cb);
        _data = grown;
        _position += cb;
        Writes++;
    }
    public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
    {
        _position = dwOrigin == 0 ? dlibMove : dwOrigin == 1 ? _position + dlibMove : _data.Length + dlibMove;
    }
    public void SetSize(long libNewSize) => Array.Resize(ref _data, (int)libNewSize);
    public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => throw new NotSupportedException();
    public void Commit(int grfCommitFlags) { }
    public void Revert() { }
    public void LockRegion(long libOffset, long cb, int dwLockType) { }
    public void UnlockRegion(long libOffset, long cb, int dwLockType) { }
    public void Stat(out STATSTG pstatstg, int grfStatFlag)
    {
        pstatstg = new STATSTG { cbSize = _data.Length, type = 2, pwcsName = grfStatFlag == 0 ? "memory" : null! };
    }
    public void Clone(out IStream ppstm) => ppstm = this;
}
sealed class Strings : IEnumString
{
    private readonly string[] _items = { "a", "b", "c" };
    private int _index;
    public int Next(int celt, string[] rgelt, IntPtr pceltFetched)
    {
        int fetched = 0;
        while (fetched < celt && _index < _items.Length) rgelt[fetched++] = _items[_index++];
        return fetched == celt ? 0 : 1;
    }
    public int Skip(int celt) { _index += celt; return _index <= _items.Length ? 0 : 1; }
    public void Reset() => _index = 0;
    public void Clone(out IEnumString ppenum) => ppenum = new Strings { _index = _index };
}
sealed class Sink : IAdviseSink
{
    public int Changes;
    public void OnDataChange(ref FORMATETC format, ref STGMEDIUM stgmedium) => Changes += format.cfFormat;
    public void OnViewChange(int aspect, int index) => Changes += aspect;
    public void OnRename(IMoniker moniker) { }
    public void OnSave() => Changes++;
    public void OnClose() { }
}
public static class EntryPoint
{
    public static int Run(int input)
    {
        // A managed IStream through the interface; optional out pointers are null.
        IStream stream = new MemoryStream();
        stream.Write(new byte[] { 1, 2, 3, 4 }, 4, IntPtr.Zero);
        stream.Seek(1, 0, IntPtr.Zero);
        var buffer = new byte[8];
        stream.Read(buffer, 8, IntPtr.Zero);
        if (buffer[0] != 2 || buffer[2] != 4 || buffer[3] != 0) return 1;
        stream.SetSize(2);
        stream.Stat(out STATSTG stat, 0);
        if (stat.cbSize != 2 || stat.type != 2 || stat.pwcsName != "memory") return 2;
        stream.Stat(out stat, 1);
        if (stat.pwcsName != null) return 3;
        stream.Clone(out IStream clone);
        if (!ReferenceEquals(clone, stream) || ((MemoryStream)stream).Writes != 1) return 4;
        if (stream is not IStream || new object() is IStream) return 5;

        // A managed enumerator and sink.
        IEnumString strings = new Strings();
        var items = new string[2];
        if (strings.Next(2, items, IntPtr.Zero) != 0 || items[0] != "a" || items[1] != "b") return 6;
        if (strings.Next(2, items, IntPtr.Zero) != 1 || items[0] != "c") return 7;
        strings.Reset();
        strings.Clone(out IEnumString copy);
        if (copy.Skip(3) != 0 || copy.Skip(1) != 1) return 8;
        IAdviseSink sink = new Sink();
        var format = new FORMATETC { cfFormat = 13, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        var medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = IntPtr.Zero };
        sink.OnDataChange(ref format, ref medium);
        sink.OnViewChange(4, 0);
        sink.OnSave();
        if (((Sink)sink).Changes != 18) return 9;

        // Struct layouts and enum values keep the upstream COM definitions.
        if (Unsafe.SizeOf<FILETIME>() != 8 || Unsafe.SizeOf<BINDPTR>() != IntPtr.Size || Unsafe.SizeOf<VARDESC.DESCUNION>() != IntPtr.Size) return 10;
        var data = new CONNECTDATA { pUnk = sink, dwCookie = 7 };
        if (!ReferenceEquals(data.pUnk, sink) || data.dwCookie != 7) return 11;
        var time = new FILETIME { dwLowDateTime = 1, dwHighDateTime = 2 };
        var bind = new BIND_OPTS { cbStruct = 16, grfMode = 3, dwTickCountDeadline = 4 };
        if (time.dwHighDateTime != 2 || bind.grfMode != 3) return 12;
        if ((int)TYMED.TYMED_ENHMF != 64 || (int)ADVF.ADVFCACHE_ONSAVE != 32 || (int)DATADIR.DATADIR_SET != 2) return 13;
        if ((int)TYPEKIND.TKIND_ALIAS != 6 || (int)INVOKEKIND.INVOKE_PROPERTYPUTREF != 8 || (int)FUNCFLAGS.FUNCFLAG_FIMMEDIATEBIND != 0x1000) return 14;
        if ((int)TYPEFLAGS.TYPEFLAG_FPROXY != 0x4000 || (short)VARFLAGS.VARFLAG_FIMMEDIATEBIND != 0x1000 || (int)SYSKIND.SYS_WIN64 != 3) return 15;
        if ((int)CALLCONV.CC_MAX != 9 || (int)DESCKIND.DESCKIND_MAX != 5 || (short)IDLFLAG.IDLFLAG_FRETVAL != 8 || (short)PARAMFLAG.PARAMFLAG_FHASCUSTDATA != 0x40) return 16;
        var attr = new TYPEATTR { cbSizeInstance = 4, typekind = TYPEKIND.TKIND_RECORD, wTypeFlags = TYPEFLAGS.TYPEFLAG_FHIDDEN };
        if (attr.typekind != TYPEKIND.TKIND_RECORD || TYPEATTR.MEMBER_ID_NIL != -1) return 17;
        return input + 1;
    }
}
