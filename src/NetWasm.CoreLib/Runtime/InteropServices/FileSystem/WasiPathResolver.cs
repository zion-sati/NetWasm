namespace System.Runtime.InteropServices.FileSystem;

using System.Runtime.InteropServices.TimeZones;

// A preopened directory as an absolute mount point: "/data" from "/data", "data" or "/data/";
// "/" from "/", "." or "".
internal readonly struct WasiMount
{
    internal WasiMount(int directory, string path)
    {
        Directory = directory;
        Path = path;
    }

    internal int Directory { get; }

    internal string Path { get; }
}

internal interface IWasiMountTable
{
    // The mounts, longest mount point first, read from the host once.
    WasiMount[] Read();
}

internal sealed class WasiMountTable(IPreopenedDirectorySource preopens) : IWasiMountTable
{
    private readonly IPreopenedDirectorySource _preopens = preopens ?? throw new ArgumentNullException(nameof(preopens));
    private WasiMount[]? _mounts;

    public WasiMount[] Read()
    {
        if (_mounts is not null)
        {
            return _mounts;
        }

        var directories = _preopens.Read();
        var mounts = new WasiMount[directories.Length];
        for (var index = 0; index < directories.Length; index++)
        {
            mounts[index] = new WasiMount(directories[index].Handle, Normalize(directories[index].Path));
        }

        Array.Sort(mounts, static (left, right) => right.Path.Length.CompareTo(left.Path.Length));
        return _mounts = mounts;
    }

    private static string Normalize(string path)
    {
        var trimmed = path.Trim('/');
        if (trimmed is "" or ".")
        {
            return "/";
        }

        return "/" + (trimmed.StartsWith("./", StringComparison.Ordinal) ? trimmed[2..] : trimmed);
    }
}

internal enum WasiPathKind
{
    // Inside a mount: its directory and the path relative to it ("." for the mount point).
    Mapped,

    // An ancestor of a mount point, such as "/" when only "/data" is mounted: a directory that
    // exists but that the program cannot change.
    Ancestor,

    // Outside every mount.
    Unmapped,
}

internal readonly struct WasiPath
{
    internal WasiPath(WasiPathKind kind, int directory, string relative)
    {
        Kind = kind;
        Directory = directory;
        Relative = relative;
    }

    internal WasiPathKind Kind { get; }

    internal int Directory { get; }

    internal string Relative { get; }
}

internal interface IWasiPathResolver
{
    WasiPath Resolve(string path);
}

internal sealed class WasiPathResolver(IWasiMountTable mounts) : IWasiPathResolver
{
    private readonly IWasiMountTable _mounts = mounts ?? throw new ArgumentNullException(nameof(mounts));

    public WasiPath Resolve(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Length == 0 || path[0] != '/')
        {
            return new WasiPath(WasiPathKind.Unmapped, 0, string.Empty);
        }

        if (path.Length > 1)
        {
            path = path.TrimEnd('/');
        }

        var mounts = _mounts.Read();
        foreach (var mount in mounts)
        {
            if (mount.Path == "/")
            {
                return new WasiPath(WasiPathKind.Mapped, mount.Directory, path.Length == 1 ? "." : path[1..]);
            }

            if (path == mount.Path)
            {
                return new WasiPath(WasiPathKind.Mapped, mount.Directory, ".");
            }

            if (path.Length > mount.Path.Length && path[mount.Path.Length] == '/'
                && path.StartsWith(mount.Path, StringComparison.Ordinal))
            {
                return new WasiPath(WasiPathKind.Mapped, mount.Directory, path[(mount.Path.Length + 1)..]);
            }
        }

        if (path == "/")
        {
            return new WasiPath(WasiPathKind.Ancestor, 0, string.Empty);
        }

        foreach (var mount in mounts)
        {
            if (mount.Path.Length > path.Length && mount.Path[path.Length] == '/'
                && mount.Path.StartsWith(path, StringComparison.Ordinal))
            {
                return new WasiPath(WasiPathKind.Ancestor, 0, string.Empty);
            }
        }

        return new WasiPath(WasiPathKind.Unmapped, 0, string.Empty);
    }
}
