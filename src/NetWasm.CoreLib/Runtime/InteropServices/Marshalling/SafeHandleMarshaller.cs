// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System.Runtime.InteropServices.Marshalling
{
    /// <summary>
    /// Marshals a SafeHandle-derived type as a borrowed unmanaged handle for the
    /// duration of a native call.
    /// </summary>
    [CustomMarshaller(
        typeof(CustomMarshallerAttribute.GenericPlaceholder),
        MarshalMode.ManagedToUnmanagedIn,
        typeof(SafeHandleMarshaller<>.ManagedToUnmanagedIn))]
    public static class SafeHandleMarshaller<T> where T : SafeHandle
    {
        public struct ManagedToUnmanagedIn
        {
            private bool _addRefd;
            private T? _handle;

            public void FromManaged(T handle)
            {
                _handle = handle;
                handle.DangerousAddRef(ref _addRefd);
            }

            public IntPtr ToUnmanaged() => _handle!.DangerousGetHandle();

            public void Free()
            {
                if (_addRefd)
                {
                    _handle!.DangerousRelease();
                }
            }
        }
    }
}
