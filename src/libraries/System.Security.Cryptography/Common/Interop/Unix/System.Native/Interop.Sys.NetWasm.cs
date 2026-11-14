// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// NetWasm: the System.Native calls dotnet/runtime deae2e32's (.NET 11.0.0) X.509 stores and PersistedFiles make
// (Interop.Stat.cs, Interop.GetEUid.cs and Interop.GetPwUid.cs), with their names and types. There is no System.Native,
// so they answer from the public file APIs as NetWasm's CoreLib answers its own (Interop.FileSystem.NetWasm.cs): the
// effective user owns every entry, the user database has no entry, and an entry's inode is a hash of the path it was
// read through. A failure records its errno and returns -1, as the native call would.

using System;
using System.IO;

internal static partial class Interop
{
    internal static partial class Sys
    {
        // The fields WASI has no counterpart for stay zero, as they do in NetWasm's CoreLib.
#pragma warning disable CS0649
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
        }

#pragma warning restore CS0649

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

        private const int FileMode = FileTypes.S_IFREG | 0x1B6;
        private const int DirectoryMode = FileTypes.S_IFDIR | 0x1FF;

        // Follows links, as stat does.
        internal static int Stat(string path, out FileStatus output)
        {
            output = default;
            try
            {
                FileSystemInfo entry = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
                if (!entry.Exists)
                {
                    return SetLastError(Error.ENOENT);
                }

                output.Mode = entry is DirectoryInfo ? DirectoryMode : FileMode;
                output.Size = entry is FileInfo file ? file.Length : 0;
                (output.ATime, output.ATimeNsec) = ToUnixTime(entry.LastAccessTimeUtc);
                (output.MTime, output.MTimeNsec) = ToUnixTime(entry.LastWriteTimeUtc);
                (output.CTime, output.CTimeNsec) = (output.MTime, output.MTimeNsec);
                output.Ino = Identity(path);
                return 0;
            }
            catch (UnauthorizedAccessException)
            {
                return SetLastError(Error.EACCES);
            }
            catch (IOException)
            {
                return SetLastError(Error.EIO);
            }
        }

        internal static uint GetEUid() => 0;

#pragma warning disable CS0649
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
#pragma warning restore CS0649

        // WASI has no user database: the user has no entry, which getpwuid_r reports as -1 and callers read as no home
        // directory.
        internal static unsafe int GetPwUidR(uint uid, out Passwd pwd, byte* buf, int bufLen)
        {
            pwd = default;
            return -1;
        }

        private static (long Seconds, long Nanoseconds) ToUnixTime(DateTime time)
        {
            long ticks = time.Ticks - DateTime.UnixEpoch.Ticks;
            long seconds = Math.DivRem(ticks, TimeSpan.TicksPerSecond, out long remainder);
            return (seconds, remainder * TimeSpan.NanosecondsPerTick);
        }

        // FNV-1a over the path's characters: equal paths name one file.
        private static long Identity(string path)
        {
            unchecked
            {
                var hash = (long)0xcbf29ce484222325;
                foreach (char character in path)
                {
                    hash = (hash ^ character) * 0x100000001b3;
                }

                return hash;
            }
        }
    }
}
