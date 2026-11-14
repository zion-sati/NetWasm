using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TUnit.Assertions;
using TUnit.Core;

namespace NetWasm.System.IO.FileSystem.DriveInfo.Tests;

// RC3's DriveInfo for builds without an OS target (browser and WASI): the drives are Environment.GetLogicalDrives(),
// "/" on NetWasm, and a WASI file system reports no type, format or sizes.
public sealed class DriveInfoTests
{
    [Test]
    public async Task TheRootIsADriveThatIsReady()
    {
        var root = global::System.IO.DriveInfo.GetDrives().Single(drive => drive.Name == "/");

        await Assert.That(root.IsReady).IsTrue();
        await Assert.That(root.RootDirectory.FullName).IsEqualTo("/");
        await Assert.That(root.ToString()).IsEqualTo("/");
        await Assert.That(new global::System.IO.DriveInfo("/").Name).IsEqualTo("/");
#if NETWASM
        await Assert.That(root.DriveType).IsEqualTo(DriveType.Unknown);
        await Assert.That(root.DriveFormat).IsEqualTo("memfs");
        await Assert.That(root.TotalSize).IsEqualTo(0L);
        await Assert.That(root.AvailableFreeSpace).IsEqualTo(0L);
#endif
    }

    [Test]
    public async Task DriveNamesAreValidated()
    {
        var empty = false;
        var nul = false;
        var missing = false;
        try { _ = new global::System.IO.DriveInfo(""); } catch (ArgumentException) { empty = true; }
        try { _ = new global::System.IO.DriveInfo("a\0b"); } catch (ArgumentException) { nul = true; }
        try { _ = new global::System.IO.DriveInfo(null!); } catch (ArgumentNullException) { missing = true; }

        await Assert.That(empty && nul && missing).IsTrue();
    }
}
