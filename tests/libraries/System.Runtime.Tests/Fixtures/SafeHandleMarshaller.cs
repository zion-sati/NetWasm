// From the compiler harness's SafeHandleMarshallerCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
namespace SafeHandleMarshallerFixture;

sealed class Handle : SafeHandle
{
    public static int Released;
    public Handle() : base(IntPtr.Zero, ownsHandle: true) { }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle()
    {
        Released++;
        return true;
    }
}

sealed class Hidden : SafeHandle
{
    private Hidden() : base(IntPtr.Zero, ownsHandle: true) { }
    public override bool IsInvalid => handle == IntPtr.Zero;
    protected override bool ReleaseHandle() => true;
}

public static class EntryPoint
{
    public static int Run(int seed)
    {
        // A result: the handle exists before the call, and the native value initializes it.
        var result = new SafeHandleMarshaller<Handle>.ManagedToUnmanagedOut();
        result.FromUnmanaged((IntPtr)42);
        Handle created = result.ToManaged();
        result.Free();
        if (created.DangerousGetHandle() != (IntPtr)42 || created.IsClosed || created.IsInvalid) return 1;
        created.Dispose();
        if (Handle.Released != 1 || !created.IsClosed) return 2;

        // A call that never produced a value disposes the unused handle (invalid, so nothing is released).
        var unused = new SafeHandleMarshaller<Handle>.ManagedToUnmanagedOut();
        Handle never = unused.ToManaged();
        unused.Free();
        if (!never.IsClosed || Handle.Released != 1) return 3;

        // An argument: referenced for the call, released after.
        var original = new Handle();
        Marshal.InitHandle(original, (IntPtr)7);
        var argument = new SafeHandleMarshaller<Handle>.ManagedToUnmanagedIn();
        argument.FromManaged(original);
        if (argument.ToUnmanaged() != (IntPtr)7) return 4;
        argument.Free();

        // By reference, unchanged: the caller keeps its handle, and the spare one is never used.
        var same = new SafeHandleMarshaller<Handle>.ManagedToUnmanagedRef();
        same.FromManaged(original);
        if (same.ToUnmanaged() != (IntPtr)7) return 5;
        same.OnInvoked();
        same.FromUnmanaged((IntPtr)7);
        if (!ReferenceEquals(same.ToManagedFinally(), original)) return 6;
        same.Free();

        // By reference, replaced: a new handle carries the new value.
        var replaced = new SafeHandleMarshaller<Handle>.ManagedToUnmanagedRef();
        replaced.FromManaged(original);
        replaced.OnInvoked();
        replaced.FromUnmanaged((IntPtr)9);
        Handle fresh = replaced.ToManagedFinally();
        replaced.Free();
        if (ReferenceEquals(fresh, original) || fresh.DangerousGetHandle() != (IntPtr)9) return 7;

        // By reference, never invoked: the spare handle is disposed.
        var abandoned = new SafeHandleMarshaller<Handle>.ManagedToUnmanagedRef();
        abandoned.FromManaged(original);
        abandoned.Free();

        original.Dispose();
        fresh.Dispose();
        if (Handle.Released != 3) return 8;

        // As .NET's Activator, a non-public parameterless constructor is not used.
        try
        {
            _ = new SafeHandleMarshaller<Hidden>.ManagedToUnmanagedOut();
            return 9;
        }
        catch (MissingMethodException)
        {
        }
        return seed + 1;
    }
}
