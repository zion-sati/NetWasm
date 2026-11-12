namespace System.Runtime.InteropServices.FileSystem;

using System.Runtime.InteropServices.WebAssembly;

// The canonical ABI layouts of wasi:filesystem's values, in a pointer of P bytes (4 for wasm32,
// 8 for wasm64). A string is a pointer and a length. error-code is a variant whose
// other(option<string>) case makes it 4P bytes, P-aligned; descriptor-type likewise. An instant
// is { seconds: s64, nanoseconds: u32 }, 16 bytes, 8-aligned.
internal static class WasiFileSystemLayout
{
    internal const int Blocked = -1;
    internal const uint Completed = 0;
    internal const uint Dropped = 1;

    private const byte DirectoryType = 2;
    private const byte SymbolicLinkType = 4;
    private const byte RegularFileType = 5;
    private const byte OtherType = 7;
    private const byte OtherError = 36;
    private const long TicksPerSecond = 10_000_000;

    internal static nuint Pointer => unchecked((nuint)nuint.Size);

    // result<_, error-code> and result<descriptor | string, error-code>: the discriminant, then
    // the payload at P.
    internal static nuint SmallResultSize => Pointer * 5;

    // open-at's parameters: descriptor, path-flags at 4, path at 8, open-flags at 8 + 2P and
    // descriptor-flags after them, P-aligned.
    internal static nuint OpenAtParametersSize => (Pointer * 3 + 9) & ~(Pointer - 1);

    // descriptor-stat: type (4P), link-count, size, then three option<instant> (24 bytes each).
    internal static nuint StatSize => Pointer * 4 + 88;

    // result<descriptor-stat, error-code>: the payload at 8, the stat's alignment.
    internal static nuint StatResultSize => 8 + StatSize;

    // directory-entry: type (4P), then name.
    internal static nuint DirectoryEntrySize => Pointer * 6;

    // The error-code variant at address, after freeing a string the host gave other(...).
    internal static FileSystemError ReadError(nuint address)
    {
        var code = CanonicalAbi.ReadByte(address, 0);
        if (code == OtherError)
        {
            FreeOptionalString(address + Pointer);
        }

        return code switch
        {
            0 or 30 => FileSystemError.AccessDenied,
            2 => FileSystemError.BadHandle,
            3 or 34 => FileSystemError.Busy,
            5 or 22 => FileSystemError.NoSpace,
            6 => FileSystemError.AlreadyExists,
            7 or 29 => FileSystemError.FileTooLarge,
            8 or 11 or 33 => FileSystemError.InvalidArgument,
            13 => FileSystemError.IsDirectory,
            14 => FileSystemError.Loop,
            17 => FileSystemError.NameTooLong,
            19 => FileSystemError.NotFound,
            23 => FileSystemError.NotDirectory,
            24 => FileSystemError.NotEmpty,
            26 => FileSystemError.Unsupported,
            32 => FileSystemError.ReadOnly,
            35 => FileSystemError.CrossDevice,
            _ => FileSystemError.IOError,
        };
    }

    // A result whose error-code payload lies at payloadOffset: None when it is ok.
    internal static FileSystemError ReadResult(nuint result, nuint payloadOffset) =>
        CanonicalAbi.ReadByte(result, 0) switch
        {
            0 => FileSystemError.None,
            1 => ReadError(result + payloadOffset),
            _ => FileSystemError.IOError,
        };

    // A descriptor-type at address, after freeing a string the host gave other(...).
    internal static FileSystemEntryKind ReadKind(nuint address)
    {
        var type = CanonicalAbi.ReadByte(address, 0);
        if (type == OtherType)
        {
            FreeOptionalString(address + Pointer);
        }

        return type switch
        {
            DirectoryType => FileSystemEntryKind.Directory,
            SymbolicLinkType => FileSystemEntryKind.SymbolicLink,
            RegularFileType => FileSystemEntryKind.File,
            _ => FileSystemEntryKind.Other,
        };
    }

    // The descriptor-stat at address.
    internal static FileSystemEntryStatus ReadStat(nuint address)
    {
        var times = address + Pointer * 4 + 16;
        return new FileSystemEntryStatus(
            ReadKind(address),
            CanonicalAbi.ReadInt64(address, Pointer * 4 + 8),
            ReadOptionalInstant(times),
            ReadOptionalInstant(times + 24),
            ReadOptionalInstant(times + 48));
    }

    // A string the host allocated, freed once read.
    internal static string TakeString(nuint address)
    {
        var pointer = CanonicalAbi.ReadAddress(address, 0);
        var length = CanonicalAbi.ReadAddress(address, Pointer);
        var value = CanonicalAbi.LiftString(pointer, length);
        if (length != 0)
        {
            CanonicalAbi.Free(pointer);
        }

        return value;
    }

    internal static void WriteString(nuint address, CanonicalBuffer value)
    {
        CanonicalAbi.WriteAddress(address, 0, value.Address);
        CanonicalAbi.WriteAddress(address, Pointer, value.Length);
    }

    // A new-timestamp at address: no-change for null, otherwise timestamp(instant).
    internal static void WriteNewTimestamp(nuint address, DateTime? value)
    {
        if (value is not { } time)
        {
            CanonicalAbi.WriteByte(address, 0, 0);
            return;
        }

        var ticks = time.Ticks - DateTime.UnixEpoch.Ticks;
        var seconds = ticks / TicksPerSecond;
        var remainder = ticks % TicksPerSecond;
        if (remainder < 0)
        {
            seconds--;
            remainder += TicksPerSecond;
        }

        CanonicalAbi.WriteByte(address, 0, 2);
        CanonicalAbi.WriteInt64(address, 8, seconds);
        CanonicalAbi.WriteInt32(address, 16, unchecked((int)(remainder * 100)));
    }

    // An option<instant> at address, as a UTC DateTime; the Unix epoch when the host has none.
    private static DateTime ReadOptionalInstant(nuint address)
    {
        if (CanonicalAbi.ReadByte(address, 0) == 0)
        {
            return DateTime.UnixEpoch;
        }

        var seconds = CanonicalAbi.ReadInt64(address, 8);
        var nanoseconds = unchecked((uint)CanonicalAbi.ReadInt32(address, 16));
        return new DateTime(
            DateTime.UnixEpoch.Ticks + seconds * TicksPerSecond + nanoseconds / 100,
            DateTimeKind.Utc);
    }

    private static void FreeOptionalString(nuint option)
    {
        if (CanonicalAbi.ReadByte(option, 0) != 0)
        {
            TakeString(option + Pointer);
        }
    }
}
