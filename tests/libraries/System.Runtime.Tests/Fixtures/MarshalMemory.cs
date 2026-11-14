// From the compiler harness's MarshalMemoryCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Runtime.InteropServices;
using System.Security;
namespace MarshalFixture;
public static class EntryPoint
{
    static bool Throws<T>(Action action) where T : Exception
    {
        try { action(); return false; } catch (T) { return true; }
    }

    public static int Run(int input)
    {
        if (Marshal.SystemDefaultCharSize != 2 || Marshal.SystemMaxDBCSCharSize != 3) return 1;

        // Pointer reads and writes, aligned and unaligned.
        IntPtr block = Marshal.AllocHGlobal(64);
        try
        {
            Marshal.WriteByte(block, 0x7F);
            Marshal.WriteInt16(block, 1, (short)-2);
            Marshal.WriteInt16(block, 4, 'A');
            Marshal.WriteInt32(block, 7, 0x12345678);
            Marshal.WriteInt64(block, 16, 0x0102030405060708L);
            Marshal.WriteIntPtr(block, 24, (IntPtr)(-5));
            if (Marshal.ReadByte(block) != 0x7F || Marshal.ReadInt16(block, 1) != -2 || Marshal.ReadInt16(block, 4) != 'A') return 2;
            if (Marshal.ReadInt32(block, 7) != 0x12345678 || Marshal.ReadInt64(block, 16) != 0x0102030405060708L || Marshal.ReadIntPtr(block, 24) != (IntPtr)(-5)) return 3;
            if (Marshal.ReadByte(block, 16) != 0x08 || Marshal.ReadInt32(block + 16) != 0x05060708) return 4;

            // Copies between managed arrays and native memory.
            Marshal.Copy(new[] { 1, 2, 3, 4 }, 1, block, 2);
            var ints = new int[4];
            Marshal.Copy(block, ints, 2, 2);
            if (ints[0] != 0 || ints[2] != 2 || ints[3] != 3) return 5;
            Marshal.Copy(new[] { 1.5, -2.25 }, 0, block, 2);
            var doubles = new double[2];
            Marshal.Copy(block, doubles, 0, 2);
            Marshal.Copy("hey".ToCharArray(), 0, block + 32, 3);
            var chars = new char[3];
            Marshal.Copy(block + 32, chars, 0, 3);
            if (doubles[1] != -2.25 || new string(chars) != "hey") return 6;
            Marshal.Copy(new long[] { -1 }, 0, block, 1);
            Marshal.Copy(new short[] { 7 }, 0, block, 1);
            Marshal.Copy(new float[] { 2.5f }, 0, block + 8, 1);
            Marshal.Copy(new byte[] { 9, 8 }, 0, block + 12, 2);
            Marshal.Copy(new IntPtr[] { (IntPtr)77 }, 0, block + 40, 1);
            var longs = new long[1]; var shorts = new short[1]; var floats = new float[1]; var bytes = new byte[2]; var pointers = new IntPtr[1];
            Marshal.Copy(block, longs, 0, 1); Marshal.Copy(block, shorts, 0, 1); Marshal.Copy(block + 8, floats, 0, 1);
            Marshal.Copy(block + 12, bytes, 0, 2); Marshal.Copy(block + 40, pointers, 0, 1);
            if (longs[0] != unchecked((long)0xFFFFFFFFFFFF0007) || shorts[0] != 7 || floats[0] != 2.5f || bytes[1] != 8 || pointers[0] != (IntPtr)77) return 7;
            if (!Throws<ArgumentNullException>(() => Marshal.Copy((int[])null!, 0, block, 1)) || !Throws<ArgumentNullException>(() => Marshal.Copy(new int[1], 0, IntPtr.Zero, 1))) return 8;
            if (!Throws<ArgumentOutOfRangeException>(() => Marshal.Copy(new int[1], 1, block, 1)) || !Throws<ArgumentOutOfRangeException>(() => Marshal.Copy(block, new int[1], -1, 1))) return 9;

            // Reallocation keeps contents; a zero-sized CoTaskMem reallocation frees.
            block = Marshal.ReAllocHGlobal(block, 128);
            if (Marshal.ReadInt32(block + 32) != 0x00650068) return 10;
        }
        finally
        {
            Marshal.FreeHGlobal(block);
        }
        IntPtr task = Marshal.AllocCoTaskMem(8);
        Marshal.WriteInt32(task, 99);
        task = Marshal.ReAllocCoTaskMem(task, 16);
        if (Marshal.ReadInt32(task) != 99 || Marshal.ReAllocCoTaskMem(task, 0) != IntPtr.Zero) return 11;

        // ANSI (UTF-8 here, as on Unix), Unicode, UTF-8, Auto and BSTR strings.
        const string text = "héllo";
        IntPtr ansi = Marshal.StringToHGlobalAnsi(text), uni = Marshal.StringToHGlobalUni(text), auto = Marshal.StringToHGlobalAuto(text);
        IntPtr coAnsi = Marshal.StringToCoTaskMemAnsi(text), coUni = Marshal.StringToCoTaskMemUni(text), coUtf8 = Marshal.StringToCoTaskMemUTF8(text), coAuto = Marshal.StringToCoTaskMemAuto(text);
        if (Marshal.PtrToStringAnsi(ansi) != text || Marshal.PtrToStringAnsi(ansi, 3) != "hé" || Marshal.PtrToStringUni(uni) != text || Marshal.PtrToStringUni(uni, 2) != "hé") return 12;
        if (Marshal.PtrToStringAuto(auto) != text || Marshal.PtrToStringUTF8(coUtf8) != text || Marshal.PtrToStringAnsi(coAnsi) != text || Marshal.PtrToStringUni(coUni) != text || Marshal.PtrToStringAuto(coAuto, 3) != "hé") return 13;
        if (Marshal.ReadByte(ansi, 6) != 0 || Marshal.ReadInt16(uni, 10) != 0) return 14;
        Marshal.ZeroFreeGlobalAllocAnsi(ansi); Marshal.ZeroFreeGlobalAllocUnicode(uni); Marshal.FreeHGlobal(auto);
        Marshal.ZeroFreeCoTaskMemAnsi(coAnsi); Marshal.ZeroFreeCoTaskMemUnicode(coUni); Marshal.ZeroFreeCoTaskMemUTF8(coUtf8); Marshal.FreeCoTaskMem(coAuto);
        if (Marshal.StringToHGlobalAnsi(null) != IntPtr.Zero || Marshal.StringToCoTaskMemUni(null) != IntPtr.Zero || Marshal.StringToBSTR(null) != IntPtr.Zero) return 15;
        if (Marshal.PtrToStringAnsi(IntPtr.Zero) != null || Marshal.PtrToStringUni(IntPtr.Zero) != null || Marshal.PtrToStringAuto(IntPtr.Zero) != null) return 16;
        if (!Throws<ArgumentOutOfRangeException>(() => Marshal.PtrToStringUni((IntPtr)8, -1))) return 17;
        IntPtr bstr = Marshal.StringToBSTR("abc");
        if (Marshal.PtrToStringBSTR(bstr) != "abc" || Marshal.ReadInt32(bstr, -4) != 6 || Marshal.ReadInt16(bstr, 6) != 0) return 18;
        Marshal.ZeroFreeBSTR(bstr);
        Marshal.ZeroFreeBSTR(IntPtr.Zero);
        Marshal.FreeBSTR(IntPtr.Zero);

        // SecureString conversions.
        using (var secure = new SecureString())
        {
            foreach (char c in "pwé") secure.AppendChar(c);
            IntPtr p1 = Marshal.SecureStringToGlobalAllocUnicode(secure), p2 = Marshal.SecureStringToGlobalAllocAnsi(secure);
            IntPtr p3 = Marshal.SecureStringToCoTaskMemUnicode(secure), p4 = Marshal.SecureStringToCoTaskMemAnsi(secure), p5 = Marshal.SecureStringToBSTR(secure);
            if (Marshal.PtrToStringUni(p1) != "pwé" || Marshal.PtrToStringAnsi(p2) != "pwé" || Marshal.PtrToStringUni(p3) != "pwé" || Marshal.PtrToStringAnsi(p4) != "pwé" || Marshal.PtrToStringBSTR(p5) != "pwé") return 19;
            Marshal.ZeroFreeGlobalAllocUnicode(p1); Marshal.ZeroFreeGlobalAllocAnsi(p2); Marshal.ZeroFreeCoTaskMemUnicode(p3); Marshal.ZeroFreeCoTaskMemAnsi(p4); Marshal.ZeroFreeBSTR(p5);
        }
        try { Marshal.SecureStringToBSTR(null!); return 20; } catch (ArgumentNullException e) { if (e.ParamName != "s") return 21; }

        // Element addresses inside a pinned array.
        var pinned = new[] { 10, 20, 30 };
        var handle = GCHandle.Alloc(pinned, GCHandleType.Pinned);
        try
        {
            IntPtr first = Marshal.UnsafeAddrOfPinnedArrayElement(pinned, 0), third = Marshal.UnsafeAddrOfPinnedArrayElement((Array)pinned, 2);
            if (first != handle.AddrOfPinnedObject() || third - first != 8 || Marshal.ReadInt32(third) != 30) return 22;
        }
        finally
        {
            handle.Free();
        }

        // HRESULT mapping and the no-COM profile.
        if (Marshal.GetExceptionForHR(0) != null || Marshal.GetExceptionForHR(1, IntPtr.Zero) != null) return 23;
        var argument = Marshal.GetExceptionForHR(unchecked((int)0x80070057));
        if (argument is not ArgumentException || argument.HResult != unchecked((int)0x80070057)) return 24;
        if (Marshal.GetExceptionForHR(unchecked((int)0x80131509)) is not InvalidOperationException) return 25;
        var unknown = Marshal.GetExceptionForHR(unchecked((int)0x81234567));
        if (unknown is not COMException || unknown.HResult != unchecked((int)0x81234567)) return 26;
        if (!Throws<NotSupportedException>(() => Marshal.ThrowExceptionForHR(unchecked((int)0x80131515), IntPtr.Zero))) return 27;
        Marshal.ThrowExceptionForHR(0, IntPtr.Zero);
        if (Marshal.GetHRForException(new InvalidOperationException()) != unchecked((int)0x80131509) || Marshal.GetHRForException(null) != 0) return 28;
        if (Marshal.IsComObject(new object()) || !Throws<ArgumentNullException>(() => Marshal.IsComObject(null!))) return 29;
        if (Marshal.AreComObjectsAvailableForCleanup() || Marshal.IsTypeVisibleFromCom(typeof(int)) || Marshal.GetTypeFromCLSID(Guid.Empty) != null) return 30;
        Marshal.CleanupUnusedObjectsInCurrentContext();
        if (!Throws<PlatformNotSupportedException>(() => Marshal.GetIDispatchForObject(new object())) || !Throws<PlatformNotSupportedException>(() => Marshal.ReleaseComObject(new object()))) return 31;
        if (!Throws<PlatformNotSupportedException>(() => Marshal.GetIUnknownForObject(new object())) || !Throws<PlatformNotSupportedException>(() => Marshal.BindToMoniker("m"))) return 32;
        if (!Throws<ArgumentNullException>(() => Marshal.GetHINSTANCE(null!)) || !Throws<ArgumentNullException>(() => Marshal.Prelink(null!))) return 33;
#if !DESKTOP_ORACLE
        // NetWasm boundaries: no COM vtables, no last-error state, no trampolines or type reflection.
        if (!Throws<PlatformNotSupportedException>(() => Marshal.AddRef((IntPtr)8)) || !Throws<PlatformNotSupportedException>(() => Marshal.Release((IntPtr)8))) return 34;
        if (!Throws<PlatformNotSupportedException>(() => Marshal.QueryInterface((IntPtr)8, Guid.Empty, out _))) return 35;
        Marshal.SetLastPInvokeError(2);
        if (Marshal.GetLastWin32Error() != 2 || Marshal.GetHRForLastWin32Error() != unchecked((int)0x80070002)) return 36;
        if (!Throws<PlatformNotSupportedException>(() => Marshal.GetFunctionPointerForDelegate(new Action(() => { }))) || !Throws<PlatformNotSupportedException>(() => Marshal.GetDelegateForFunctionPointer<Action>((IntPtr)8))) return 37;
        if (!Throws<PlatformNotSupportedException>(() => Marshal.GenerateGuidForType(typeof(int))) || !Throws<PlatformNotSupportedException>(() => Marshal.GenerateProgIdForType(typeof(int)))) return 38;
        if (!Throws<PlatformNotSupportedException>(() => Marshal.PrelinkAll(typeof(EntryPoint)))) return 39;
#pragma warning disable CS0618
        if (!Throws<PlatformNotSupportedException>(() => Marshal.ReadInt32(new int[1], 0)) || !Throws<PlatformNotSupportedException>(() => Marshal.WriteInt64(new long[1], 0, 1)) || !Throws<PlatformNotSupportedException>(() => Marshal.GetExceptionCode())) return 40;
#pragma warning restore CS0618
        if (!Throws<PlatformNotSupportedException>(() => Marshal.UnsafeAddrOfPinnedArrayElement((Array)new object[1], 0))) return 41;
#endif
        return input + 1;
    }
}
