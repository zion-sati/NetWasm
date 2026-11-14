// From the compiler harness's OperatingSystemContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Runtime.Serialization;
namespace OperatingSystemFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var prefixes = new[] { "Microsoft Win32S ", "Microsoft Windows 98 ", "Microsoft Windows NT ", "Microsoft Windows CE ", "Unix ", "Xbox ", "Mac OS X ", "Other " };
        var version = new Version(5, 2, 3, 4);
        for (var value = 0; value < prefixes.Length; value++)
        {
            var os = new OperatingSystem((PlatformID)value, version);
            var clone = (OperatingSystem)((ICloneable)os).Clone();
            if (os.Platform != (PlatformID)value || os.Version != version || os.ServicePack != "") return 1;
            if (os.VersionString != prefixes[value] + "5.2.3.4" || os.ToString() != os.VersionString) return 2;
            if (ReferenceEquals(os, clone) || clone.Platform != os.Platform || clone.Version != os.Version || clone.VersionString != os.VersionString) return 3;
        }
        if (new OperatingSystem(PlatformID.Win32Windows, new Version(4, 0)).VersionString != "Microsoft Windows 95 4.0") return 4;
        if (new OperatingSystem(PlatformID.Win32Windows, new Version(4, 1)).VersionString != "Microsoft Windows 98 4.1") return 5;
        var caught = 0;
        try { new OperatingSystem((PlatformID)(-1), version); } catch (ArgumentOutOfRangeException e) { if (e.ParamName == "platform") caught++; }
        try { new OperatingSystem((PlatformID)8, version); } catch (ArgumentOutOfRangeException e) { if (e.ParamName == "platform") caught++; }
        try { new OperatingSystem(PlatformID.Unix, null); } catch (ArgumentNullException e) { if (e.ParamName == "version") caught++; }
        try { new OperatingSystem(PlatformID.Unix, version).GetObjectData(null, default(StreamingContext)); } catch (PlatformNotSupportedException) { caught++; }
        return caught == 4 ? input + 1 : 6;
    }
}
