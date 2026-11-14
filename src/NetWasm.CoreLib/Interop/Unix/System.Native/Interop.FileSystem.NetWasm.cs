// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// NetWasm's counterpart of the System.Native calls that dotnet/runtime
// deae2e32bf0e018e09d64597b7c59b62b73bb3ce's Unix file APIs make
// (src/libraries/Common/src/Interop/Unix/System.Native/Interop.*.cs), with their names, types and
// results, so those APIs port unchanged. Each call is a file or directory platform service
// (System.Runtime.InteropServices.WebAssembly); a failure records its errno and returns -1, as
// the native call would. WASI has no file locking, permissions, ownership or preallocation, so
// locking and advice succeed without effect, files report rw-rw-rw- (directories rwxrwxrwx) owned
// by the effective user, and the calls behind those features fail with ENOTSUP. A status's
// inode is a hash of the path it was read through.

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WebAssembly;
using Microsoft.Win32.SafeHandles;
using PlatformServices = System.Runtime.InteropServices.PlatformServices;

// NetWasm adaptation: NetWasm's Interop class is System.Interop.
namespace System
{
    internal static partial class Interop
    {
        internal const int DefaultPathBufferSize = 4096;

        internal static partial class Sys
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct FileStatus
            {
                internal FileStatusFlags Flags;
                internal int Mode;
                internal uint Uid;
                internal uint Gid;
                internal long Size;
                internal long ATime;
                internal long ATimeNsec;
                internal long MTime;
                internal long MTimeNsec;
                internal long CTime;
                internal long CTimeNsec;
                internal long BirthTime;
                internal long BirthTimeNsec;
                internal long Dev;
                internal long RDev;
                internal long Ino;
                internal uint UserFlags;
                internal int HardLinkCount;
            }

            internal static class FileTypes
            {
                internal const int S_IFMT = 0xF000;
                internal const int S_IFIFO = 0x1000;
                internal const int S_IFCHR = 0x2000;
                internal const int S_IFDIR = 0x4000;
                internal const int S_IFBLK = 0x6000;
                internal const int S_IFREG = 0x8000;
                internal const int S_IFLNK = 0xA000;
                internal const int S_IFSOCK = 0xC000;
            }

            [Flags]
            internal enum FileStatusFlags
            {
                None = 0,
                HasBirthTime = 1,
            }

            [Flags]
            internal enum OpenFlags
            {
                O_RDONLY = 0x0000,
                O_WRONLY = 0x0001,
                O_RDWR = 0x0002,
                O_CLOEXEC = 0x0010,
                O_CREAT = 0x0020,
                O_EXCL = 0x0040,
                O_TRUNC = 0x0080,
                O_SYNC = 0x0100,
                O_NOFOLLOW = 0x0200,
            }

            internal enum SeekWhence
            {
                SEEK_SET = 0,
                SEEK_CUR = 1,
                SEEK_END = 2,
            }

            internal enum LockOperations : int
            {
                LOCK_SH = 1,
                LOCK_EX = 2,
                LOCK_NB = 4,
                LOCK_UN = 8,
            }

            internal enum LockType : short
            {
                F_WRLCK = 1,
                F_UNLCK = 2,
                F_RDLCK = 3,
            }

            internal enum FileAdvice : int
            {
                POSIX_FADV_NORMAL = 0,
                POSIX_FADV_RANDOM = 1,
                POSIX_FADV_SEQUENTIAL = 2,
                POSIX_FADV_WILLNEED = 3,
                POSIX_FADV_DONTNEED = 4,
                POSIX_FADV_NOREUSE = 5,
            }

            [Flags]
            internal enum UserFlags : uint
            {
                UF_HIDDEN = 0x8000,
            }

            internal struct TimeSpec
            {
                internal long TvSec;
                internal long TvNsec;
            }

            internal unsafe struct IOVector
            {
                public byte* Base;
                public nuint Count;
            }

            private const int FileMode = FileTypes.S_IFREG | 0x1B6;
            private const int DirectoryMode = FileTypes.S_IFDIR | 0x1FF;
            private const int LinkMode = FileTypes.S_IFLNK | 0x1FF;
            private const long NanosecondsPerTick = 100;

            internal static int Stat(string path, out FileStatus output) => GetStatus(path, followLinks: true, out output);

            internal static int LStat(string path, out FileStatus output) => GetStatus(path, followLinks: false, out output);

            internal static int Stat(ReadOnlySpan<char> path, out FileStatus output) => Stat(path.ToString(), out output);

            internal static int LStat(ReadOnlySpan<char> path, out FileStatus output) => LStat(path.ToString(), out output);

            // A handle is always an open regular file: its length, typed and owned like a file.
            internal static int FStat(SafeHandle fd, out FileStatus output)
            {
                output = default;
                var error = PlatformServices.FileSystem.GetLength(File(fd), out var length);
                if (error != FileSystemError.None)
                {
                    return Fail(error);
                }

                output.Mode = FileMode;
                output.Size = length;
                output.Ino = fd is SafeFileHandle { Path: { } path } ? Identity(path) : 0;
                return 0;
            }

            internal static SafeFileHandle Open(string path, OpenFlags flags, int mode)
            {
                System.IO.FileMode fileMode = (flags & (OpenFlags.O_CREAT | OpenFlags.O_EXCL | OpenFlags.O_TRUNC)) switch
                {
                    OpenFlags.O_CREAT | OpenFlags.O_EXCL or OpenFlags.O_CREAT | OpenFlags.O_EXCL | OpenFlags.O_TRUNC => System.IO.FileMode.CreateNew,
                    OpenFlags.O_CREAT | OpenFlags.O_TRUNC => System.IO.FileMode.Create,
                    OpenFlags.O_CREAT => System.IO.FileMode.OpenOrCreate,
                    OpenFlags.O_TRUNC => System.IO.FileMode.Truncate,
                    _ => System.IO.FileMode.Open,
                };
                FileAccess access = (flags & (OpenFlags.O_WRONLY | OpenFlags.O_RDWR)) switch
                {
                    OpenFlags.O_WRONLY => FileAccess.Write,
                    OpenFlags.O_RDWR => FileAccess.ReadWrite,
                    _ => FileAccess.Read,
                };
                FileOptions options = (flags & OpenFlags.O_SYNC) != 0 ? FileOptions.WriteThrough : FileOptions.None;

                if ((flags & OpenFlags.O_NOFOLLOW) != 0
                    && PlatformServices.FileSystem.GetStatus(path, followLinks: false, out var status) == FileSystemError.None
                    && status.Kind == FileSystemEntryKind.SymbolicLink)
                {
                    SetLastError(Error.ELOOP);
                    return new SafeFileHandle();
                }

                var error = PlatformServices.FileSystem.Open(path, fileMode, access, options, out var file);
                if (error != FileSystemError.None)
                {
                    Fail(error);
                    return new SafeFileHandle();
                }

                return new SafeFileHandle((IntPtr)file, ownsHandle: true);
            }

            internal static int Close(IntPtr fd)
            {
                PlatformServices.FileSystem.Close((long)fd);
                return 0;
            }

            internal static unsafe int PRead(SafeHandle fd, byte* buffer, int bufferSize, long fileOffset)
            {
                var error = PlatformServices.FileSystem.Read(File(fd), fileOffset, new Span<byte>(buffer, bufferSize), out var read);
                return error == FileSystemError.None ? read : Fail(error);
            }

            internal static unsafe int PWrite(SafeHandle fd, byte* buffer, int bufferSize, long fileOffset)
            {
                var error = PlatformServices.FileSystem.Write(File(fd), fileOffset, new ReadOnlySpan<byte>(buffer, bufferSize));
                return error == FileSystemError.None ? bufferSize : Fail(error);
            }

            internal static unsafe long PReadV(SafeHandle fd, IOVector* vectors, int vectorCount, long fileOffset)
            {
                long total = 0;
                for (var index = 0; index < vectorCount; index++)
                {
                    var count = checked((int)vectors[index].Count);
                    var read = PRead(fd, vectors[index].Base, count, fileOffset + total);
                    if (read < 0)
                    {
                        return total == 0 ? read : total;
                    }

                    total += read;
                    if (read < count)
                    {
                        break;
                    }
                }

                return total;
            }

            internal static unsafe long PWriteV(SafeHandle fd, IOVector* vectors, int vectorCount, long fileOffset)
            {
                long total = 0;
                for (var index = 0; index < vectorCount; index++)
                {
                    var written = PWrite(fd, vectors[index].Base, checked((int)vectors[index].Count), fileOffset + total);
                    if (written < 0)
                    {
                        return total == 0 ? written : total;
                    }

                    total += written;
                }

                return total;
            }

            // Every handle is a seekable file, read and written at offsets: sequential I/O never runs.
            internal static unsafe int Read(SafeHandle fd, byte* buffer, int count) => SetLastError(Error.ESPIPE);

            internal static unsafe int Write(SafeHandle fd, byte* buffer, int bufferSize) => SetLastError(Error.ESPIPE);

            internal static unsafe long ReadV(SafeHandle fd, IOVector* vectors, int vectorCount) => SetLastError(Error.ESPIPE);

            internal static unsafe long WriteV(SafeHandle fd, IOVector* vectors, int vectorCount) => SetLastError(Error.ESPIPE);

            // Files keep no cursor: FileStream tracks its position, so only the end needs the host.
            internal static long LSeek(SafeHandle fd, long offset, SeekWhence whence)
            {
                if (whence != SeekWhence.SEEK_END)
                {
                    return offset;
                }

                var error = PlatformServices.FileSystem.GetLength(File(fd), out var length);
                return error == FileSystemError.None ? length + offset : Fail(error);
            }

            internal static int FTruncate(SafeHandle fd, long length) => Check(PlatformServices.FileSystem.SetLength(File(fd), length));

            internal static int FSync(SafeHandle fd) => Check(PlatformServices.FileSystem.Flush(File(fd)));

            internal static int FLock(SafeHandle fd, LockOperations operation) => 0;

            internal static int FLock(IntPtr fd, LockOperations operation) => 0;

            [Flags]
            internal enum PipeFlags
            {
                O_CLOEXEC = 0x0010,
                O_NONBLOCK_READ = 0x0400,
                O_NONBLOCK_WRITE = 0x0800,
            }

            internal const int ReadEndOfPipe = 0;
            internal const int WriteEndOfPipe = 1;

            // WASI has no pipes; a handle never blocks, so it is never non-blocking either.
            internal static unsafe int Pipe(int* pipefd, PipeFlags flags) => SetLastError(Error.ENOTSUP);

            internal static unsafe int ReadFromNonblocking(SafeHandle fd, byte* buffer, int count) => SetLastError(Error.ESPIPE);

            internal static unsafe int WriteToNonblocking(SafeHandle fd, byte* buffer, int bufferSize) => SetLastError(Error.ESPIPE);

            internal static int LockFileRegion(SafeHandle fd, long offset, long length, LockType lockType) => SetLastError(Error.ENOTSUP);

            internal static bool FileSystemSupportsLocking(SafeFileHandle fd, LockOperations lockOperation, bool accessWrite) => false;

            internal static int PosixFAdvise(SafeHandle fd, long offset, long length, FileAdvice advice) => 0;

            internal static int FAllocate(SafeHandle fd, long offset, long length) => SetLastError(Error.ENOTSUP);

            internal static int MkDir(string path, int mode) => Check(PlatformServices.FileSystem.CreateDirectory(path));

            internal static int MkDir(ReadOnlySpan<char> path, int mode) => MkDir(path.ToString(), mode);

            internal static int RmDir(string path) => Check(PlatformServices.FileSystem.RemoveDirectory(path));

            internal static int Unlink(string path) => Check(PlatformServices.FileSystem.DeleteFile(path));

            internal static int Rename(string oldPath, string newPath) => Check(PlatformServices.FileSystem.Move(oldPath, newPath));

            internal static int Rename(ReadOnlySpan<char> oldPath, ReadOnlySpan<char> newPath) => Rename(oldPath.ToString(), newPath.ToString());

            internal static int Link(string source, string link) => SetLastError(Error.ENOTSUP);

            internal static int SymLink(string target, string linkPath) =>
                Check(PlatformServices.FileSystem.CreateSymbolicLink(linkPath, target));

            internal static string? ReadLink(ReadOnlySpan<char> path)
            {
                var error = PlatformServices.FileSystem.ReadLink(path.ToString(), out var target);
                if (error != FileSystemError.None)
                {
                    Fail(error);
                    return null;
                }

                return target;
            }

            internal static unsafe int UTimensat(string path, TimeSpec* times) =>
                Check(PlatformServices.FileSystem.SetTimes(path, ToDateTime(times[0]), ToDateTime(times[1])));

            internal static unsafe int FUTimens(SafeHandle fd, TimeSpec* times) =>
                fd is SafeFileHandle { Path: { } path } ? UTimensat(path, times) : SetLastError(Error.EBADF);

            internal static int ChMod(string path, int mode) => SetLastError(Error.ENOTSUP);

            internal static int FChMod(SafeHandle fd, int mode) => SetLastError(Error.ENOTSUP);

            internal static int LChflags(string path, uint flags) => SetLastError(Error.ENOTSUP);

            internal static int FChflags(SafeHandle fd, uint flags) => SetLastError(Error.ENOTSUP);

            internal static bool CanSetHiddenFlag => false;

            internal static bool SupportsHiddenFlag => false;

            internal static uint GetEUid() => 0;

            internal enum AccessMode : int
            {
                F_OK = 0,   /* Check for existence */
                X_OK = 1,   /* Check for execute */
                W_OK = 2,   /* Check for write */
                R_OK = 4,   /* Check for read */
            }

            // Answered from the entry's status: the effective user owns every entry, so the owner bits decide.
            internal static int Access(string path, AccessMode mode)
            {
                if (Stat(path, out FileStatus status) != 0)
                {
                    return -1;
                }

                int owner = (status.Mode >> 6) & 7;
                return ((int)mode & ~owner) == 0 ? 0 : SetLastError(Error.EACCES);
            }

            internal unsafe struct Passwd
            {
                internal const int InitialBufferSize = 256;
                internal byte* Name;
                internal byte* Password;
                internal uint UserId;
                internal uint GroupId;
                internal byte* UserInfo;
                internal byte* HomeDirectory;
                internal byte* Shell;
            }

            // WASI has no user database: the user has no entry, which getpwuid_r reports as -1 and callers read as no
            // home directory.
            internal static unsafe int GetPwUidR(uint uid, out Passwd pwd, byte* buf, int bufLen)
            {
                pwd = default;
                return -1;
            }

            internal static bool IsMemberOfGroup(uint gid) => true;

            // Copies the source's bytes into the destination at the start of both.
            internal static unsafe int CopyFile(SafeFileHandle source, SafeFileHandle destination, long sourceLength)
            {
                const int BufferSize = 81920;
                var buffer = new byte[BufferSize];
                long offset = 0;
                fixed (byte* pointer = buffer)
                {
                    while (true)
                    {
                        var read = PRead(source, pointer, BufferSize, offset);
                        if (read < 0)
                        {
                            return read;
                        }

                        if (read == 0)
                        {
                            return 0;
                        }

                        if (PWrite(destination, pointer, read, offset) < 0)
                        {
                            return -1;
                        }

                        offset += read;
                    }
                }
            }

            internal static string GetCwd() => Environment.CurrentDirectory;

            // mkstemps: replaces the six X's before the suffix with random characters and creates
            // that file, retrying while the name is taken; returns the open file's handle.
            internal static unsafe IntPtr MksTemps(byte* template, int suffixLength)
            {
                var length = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(template).Length;
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    FillRandom(template + length - suffixLength - 6);
                    var path = System.Text.Encoding.UTF8.GetString(template, length);
                    var error = PlatformServices.FileSystem.Open(path, System.IO.FileMode.CreateNew, FileAccess.ReadWrite, FileOptions.None, out var file);
                    if (error == FileSystemError.None)
                    {
                        return (IntPtr)file;
                    }

                    if (error != FileSystemError.AlreadyExists)
                    {
                        Fail(error);
                        return (IntPtr)(-1);
                    }
                }

                SetLastError(Error.EEXIST);
                return (IntPtr)(-1);
            }

            // mkdtemp: as mkstemps, for a directory named by a template ending in six X's.
            internal static unsafe byte* MkdTemp(byte* template)
            {
                var length = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(template).Length;
                for (var attempt = 0; attempt < 100; attempt++)
                {
                    FillRandom(template + length - 6);
                    var error = PlatformServices.FileSystem.CreateDirectory(System.Text.Encoding.UTF8.GetString(template, length));
                    if (error == FileSystemError.None)
                    {
                        return template;
                    }

                    if (error != FileSystemError.AlreadyExists)
                    {
                        Fail(error);
                        return null;
                    }
                }

                SetLastError(Error.EEXIST);
                return null;
            }

            private static unsafe void FillRandom(byte* name)
            {
                const string Characters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                Span<byte> random = stackalloc byte[6];
                PlatformServices.Random.Fill(random);
                for (var index = 0; index < 6; index++)
                {
                    name[index] = (byte)Characters[random[index] % Characters.Length];
                }
            }

            internal enum NodeType : int
            {
                DT_UNKNOWN = 0,
                DT_FIFO = 1,
                DT_CHR = 2,
                DT_DIR = 4,
                DT_BLK = 6,
                DT_REG = 8,
                DT_LNK = 10,
                DT_SOCK = 12,
                DT_WHT = 14,
            }

            [StructLayout(LayoutKind.Sequential)]
            internal unsafe struct DirectoryEntry
            {
                internal byte* Name;
                internal int NameLength;
                internal NodeType InodeType;

                internal ReadOnlySpan<char> GetName(Span<char> buffer)
                {
                    ReadOnlySpan<byte> nameBytes = (NameLength == -1)
                        ? MemoryMarshal.CreateReadOnlySpanFromNullTerminated(Name)
                        : new ReadOnlySpan<byte>(Name, NameLength);

                    return !System.Text.Encoding.UTF8.TryGetChars(nameBytes, buffer, out int charsWritten)
                        ? System.Text.Encoding.UTF8.GetString(nameBytes) // Fallback to allocation since this is a rare case
                        : buffer.Slice(0, charsWritten);
                }
            }

            // A directory opened for enumeration: its entries, read once, "." and ".." first as
            // readdir lists them, and the native copy of the name ReadDir last handed out.
            private sealed unsafe class DirectoryListing(DirectoryEntry[] entries)
            {
                internal DirectoryEntry[] Entries { get; } = entries;
                internal int Next { get; set; }
                internal byte* Name { get; set; }
                internal string[]? Names { get; init; }
            }

            private static readonly System.Collections.Generic.Dictionary<IntPtr, DirectoryListing> s_directories = new();
            private static long s_lastDirectory;

            internal static IntPtr OpenDir(string path)
            {
                var error = PlatformServices.FileSystem.ReadEntries(path, out var entries);
                if (error != FileSystemError.None)
                {
                    Fail(error);
                    return IntPtr.Zero;
                }

                var listed = new DirectoryEntry[entries.Length + 2];
                listed[0] = new DirectoryEntry { NameLength = -1, InodeType = NodeType.DT_DIR };
                listed[1] = new DirectoryEntry { NameLength = -2, InodeType = NodeType.DT_DIR };
                var names = new string[entries.Length];
                for (var index = 0; index < entries.Length; index++)
                {
                    listed[index + 2] = new DirectoryEntry
                    {
                        NameLength = index,
                        InodeType = entries[index].Kind switch
                        {
                            FileSystemEntryKind.Directory => NodeType.DT_DIR,
                            FileSystemEntryKind.File => NodeType.DT_REG,
                            FileSystemEntryKind.SymbolicLink => NodeType.DT_LNK,
                            _ => NodeType.DT_UNKNOWN,
                        },
                    };
                    names[index] = entries[index].Name;
                }

                var handle = (IntPtr)(++s_lastDirectory);
                s_directories.Add(handle, new DirectoryListing(listed) { Names = names });
                return handle;
            }

            internal static unsafe int ReadDir(IntPtr dir, DirectoryEntry* outputEntry)
            {
                var listing = s_directories[dir];
                ReleaseName(listing);
                if (listing.Next == listing.Entries.Length)
                {
                    return -1;
                }

                var entry = listing.Entries[listing.Next++];
                var name = entry.NameLength switch
                {
                    -1 => ".",
                    -2 => "..",
                    _ => listing.Names![entry.NameLength],
                };
                var length = System.Text.Encoding.UTF8.GetByteCount(name);
                var bytes = (byte*)NativeMemory.Alloc((nuint)length + 1);
                System.Text.Encoding.UTF8.GetBytes(name, new Span<byte>(bytes, length));
                bytes[length] = 0;
                listing.Name = bytes;
                outputEntry->Name = bytes;
                outputEntry->NameLength = length;
                outputEntry->InodeType = entry.InodeType;
                return 0;
            }

            internal static int CloseDir(IntPtr dir)
            {
                if (s_directories.Remove(dir, out var listing))
                {
                    ReleaseName(listing);
                }

                return 0;
            }

            private static unsafe void ReleaseName(DirectoryListing listing)
            {
                if (listing.Name != null)
                {
                    NativeMemory.Free(listing.Name);
                    listing.Name = null;
                }
            }

            private static int GetStatus(string path, bool followLinks, out FileStatus output)
            {
                output = default;
                var error = PlatformServices.FileSystem.GetStatus(path, followLinks, out var status);
                if (error != FileSystemError.None)
                {
                    return Fail(error);
                }

                output.Mode = status.Kind switch
                {
                    FileSystemEntryKind.Directory => DirectoryMode,
                    FileSystemEntryKind.SymbolicLink => LinkMode,
                    FileSystemEntryKind.File => FileMode,
                    _ => FileTypes.S_IFCHR | 0x1B6,
                };
                output.Size = status.Length;
                (output.ATime, output.ATimeNsec) = ToUnixTime(status.LastAccessTimeUtc);
                (output.MTime, output.MTimeNsec) = ToUnixTime(status.LastWriteTimeUtc);
                (output.CTime, output.CTimeNsec) = ToUnixTime(status.StatusChangeTimeUtc);
                output.Ino = Identity(path);
                output.HardLinkCount = 1;
                return 0;
            }

            private static long File(SafeHandle fd) => (long)fd.DangerousGetHandle();

            private static int Check(FileSystemError error) => error == FileSystemError.None ? 0 : Fail(error);

            private static int Fail(FileSystemError error) => SetLastError(error switch
            {
                FileSystemError.NotFound => Error.ENOENT,
                FileSystemError.AccessDenied => Error.EACCES,
                FileSystemError.AlreadyExists => Error.EEXIST,
                FileSystemError.NotDirectory => Error.ENOTDIR,
                FileSystemError.IsDirectory => Error.EISDIR,
                FileSystemError.NotEmpty => Error.ENOTEMPTY,
                FileSystemError.InvalidArgument => Error.EINVAL,
                FileSystemError.NameTooLong => Error.ENAMETOOLONG,
                FileSystemError.Loop => Error.ELOOP,
                FileSystemError.ReadOnly => Error.EROFS,
                FileSystemError.NoSpace => Error.ENOSPC,
                FileSystemError.FileTooLarge => Error.EFBIG,
                FileSystemError.Busy => Error.EBUSY,
                FileSystemError.CrossDevice => Error.EXDEV,
                FileSystemError.BadHandle => Error.EBADF,
                FileSystemError.Unsupported => Error.ENOTSUP,
                _ => Error.EIO,
            });

            private static (long Seconds, long Nanoseconds) ToUnixTime(DateTime time)
            {
                var ticks = time.Ticks - DateTime.UnixEpoch.Ticks;
                var seconds = ticks / TimeSpan.TicksPerSecond;
                var remainder = ticks % TimeSpan.TicksPerSecond;
                if (remainder < 0)
                {
                    seconds--;
                    remainder += TimeSpan.TicksPerSecond;
                }

                return (seconds, remainder * NanosecondsPerTick);
            }

            private static DateTime ToDateTime(TimeSpec time) => new(
                DateTime.UnixEpoch.Ticks + time.TvSec * TimeSpan.TicksPerSecond + time.TvNsec / NanosecondsPerTick,
                DateTimeKind.Utc);

            // FNV-1a over the path's characters: equal paths name one file.
            private static long Identity(string path)
            {
                unchecked
                {
                    var hash = (long)0xcbf29ce484222325;
                    foreach (var character in path)
                    {
                        hash = (hash ^ character) * 0x100000001b3;
                    }

                    return hash;
                }
            }
        }
    }
}
