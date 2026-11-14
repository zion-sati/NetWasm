// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Generated from dotnet/runtime commit deae2e32bf0e018e09d64597b7c59b62b73bb3ce reference sources.
// NetWasm classifies these APIs as intentionally unsupported: they bind, and every member throws
// PlatformNotSupportedException.

#pragma warning disable CS0067, CS0169, CS0618, CS0809, CS8597, CS8618, CS8625, SYSLIB0050, SYSLIB0051

namespace System.IO.MemoryMappedFiles
{
    public partial class MemoryMappedFile : System.IDisposable
    {
        internal MemoryMappedFile() { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public Microsoft.Win32.SafeHandles.SafeMemoryMappedFileHandle SafeMemoryMappedFileHandle { get { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); } }

        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateFromFile(System.IO.FileStream fileStream, string? mapName, long capacity, System.IO.MemoryMappedFiles.MemoryMappedFileAccess access, System.IO.HandleInheritability inheritability, bool leaveOpen) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateFromFile(Microsoft.Win32.SafeHandles.SafeFileHandle fileHandle, string? mapName, long capacity, System.IO.MemoryMappedFiles.MemoryMappedFileAccess access, System.IO.HandleInheritability inheritability, bool leaveOpen) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateFromFile(string path) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateFromFile(string path, System.IO.FileMode mode) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateFromFile(string path, System.IO.FileMode mode, string? mapName) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateFromFile(string path, System.IO.FileMode mode, string? mapName, long capacity) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateFromFile(string path, System.IO.FileMode mode, string? mapName, long capacity, System.IO.MemoryMappedFiles.MemoryMappedFileAccess access) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateNew(string? mapName, long capacity) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateNew(string? mapName, long capacity, System.IO.MemoryMappedFiles.MemoryMappedFileAccess access) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateNew(string? mapName, long capacity, System.IO.MemoryMappedFiles.MemoryMappedFileAccess access, System.IO.MemoryMappedFiles.MemoryMappedFileOptions options, System.IO.HandleInheritability inheritability) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        [System.Runtime.Versioning.SupportedOSPlatformAttribute("windows")]
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateOrOpen(string mapName, long capacity) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        [System.Runtime.Versioning.SupportedOSPlatformAttribute("windows")]
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateOrOpen(string mapName, long capacity, System.IO.MemoryMappedFiles.MemoryMappedFileAccess access) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        [System.Runtime.Versioning.SupportedOSPlatformAttribute("windows")]
        public static System.IO.MemoryMappedFiles.MemoryMappedFile CreateOrOpen(string mapName, long capacity, System.IO.MemoryMappedFiles.MemoryMappedFileAccess access, System.IO.MemoryMappedFiles.MemoryMappedFileOptions options, System.IO.HandleInheritability inheritability) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public System.IO.MemoryMappedFiles.MemoryMappedViewAccessor CreateViewAccessor() { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public System.IO.MemoryMappedFiles.MemoryMappedViewAccessor CreateViewAccessor(long offset, long size) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public System.IO.MemoryMappedFiles.MemoryMappedViewAccessor CreateViewAccessor(long offset, long size, System.IO.MemoryMappedFiles.MemoryMappedFileAccess access) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public System.IO.MemoryMappedFiles.MemoryMappedViewStream CreateViewStream() { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public System.IO.MemoryMappedFiles.MemoryMappedViewStream CreateViewStream(long offset, long size) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public System.IO.MemoryMappedFiles.MemoryMappedViewStream CreateViewStream(long offset, long size, System.IO.MemoryMappedFiles.MemoryMappedFileAccess access) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public void Dispose() { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        protected virtual void Dispose(bool disposing) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        [System.Runtime.Versioning.SupportedOSPlatformAttribute("windows")]
        public static System.IO.MemoryMappedFiles.MemoryMappedFile OpenExisting(string mapName) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        [System.Runtime.Versioning.SupportedOSPlatformAttribute("windows")]
        public static System.IO.MemoryMappedFiles.MemoryMappedFile OpenExisting(string mapName, System.IO.MemoryMappedFiles.MemoryMappedFileRights desiredAccessRights) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        [System.Runtime.Versioning.SupportedOSPlatformAttribute("windows")]
        public static System.IO.MemoryMappedFiles.MemoryMappedFile OpenExisting(string mapName, System.IO.MemoryMappedFiles.MemoryMappedFileRights desiredAccessRights, System.IO.HandleInheritability inheritability) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
    }

    public sealed partial class MemoryMappedViewAccessor : System.IO.UnmanagedMemoryAccessor
    {
        internal MemoryMappedViewAccessor() { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public long PointerOffset { get { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); } }

        public Microsoft.Win32.SafeHandles.SafeMemoryMappedViewHandle SafeMemoryMappedViewHandle { get { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); } }

        protected override void Dispose(bool disposing) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public void Flush() { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
    }

    public sealed partial class MemoryMappedViewStream : System.IO.UnmanagedMemoryStream
    {
        internal MemoryMappedViewStream() { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public long PointerOffset { get { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); } }

        public Microsoft.Win32.SafeHandles.SafeMemoryMappedViewHandle SafeMemoryMappedViewHandle { get { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); } }

        protected override void Dispose(bool disposing) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public override void Flush() { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
        public override void SetLength(long value) { throw new global::System.PlatformNotSupportedException("Memory-mapped files is not available on this NetWasm platform."); }
    }

    [System.FlagsAttribute]
    public enum MemoryMappedFileRights
    {
        CopyOnWrite = 1,
        Write = 2,
        Read = 4,
        ReadWrite = 6,
        Execute = 8,
        ReadExecute = 12,
        ReadWriteExecute = 14,
        Delete = 65536,
        ReadPermissions = 131072,
        ChangePermissions = 262144,
        TakeOwnership = 524288,
        FullControl = 983055,
        AccessSystemSecurity = 16777216,
    }

    public enum MemoryMappedFileAccess
    {
        ReadWrite = 0,
        Read = 1,
        Write = 2,
        CopyOnWrite = 3,
        ReadExecute = 4,
        ReadWriteExecute = 5,
    }

    [System.FlagsAttribute]
    public enum MemoryMappedFileOptions
    {
        None = 0,
        DelayAllocatePages = 67108864,
    }
}
