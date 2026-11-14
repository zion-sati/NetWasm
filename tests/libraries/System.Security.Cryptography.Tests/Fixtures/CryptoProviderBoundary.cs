// From the compiler harness's CryptoProviderBoundaryCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
#pragma warning disable SYSLIB0021, SYSLIB0022, SYSLIB0023
using System;
using System.Security.Cryptography;
namespace CryptoProviderFixture;

public static class EntryPoint
{
    private static bool Rejects(Func<object> create)
    {
        try
        {
            _ = create();
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return true;
        }
    }

    public static int Run(int value)
    {
        if (AesGcm.NonceByteSizes.MinSize != 12 || AesCcm.NonceByteSizes.MaxSize != 13) return 1;
        RSACryptoServiceProvider.UseMachineKeyStore = true;
        if (!RSACryptoServiceProvider.UseMachineKeyStore) return 2;
        RSACryptoServiceProvider.UseMachineKeyStore = false;

        if (!AesGcm.IsSupported || !AesCcm.IsSupported) return 3;
        // MD5 is managed on NetWasm, as on desktop it is the platform's.
        using (var md5 = new MD5CryptoServiceProvider())
        {
            if (Convert.ToHexString(md5.ComputeHash(new byte[] { (byte)'a', (byte)'b', (byte)'c' })) != "900150983CD24FB0D6963F7D28E17F72") return 9;
        }

        return value + 1;
    }
}
