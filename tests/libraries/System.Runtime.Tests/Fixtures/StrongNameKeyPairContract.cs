// From the compiler harness's StrongNameKeyPairContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
namespace StrongNameFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var rejected = 0; var bytes = new byte[] { 1, 2, 3 };
        try { new StrongNameKeyPair(bytes); } catch (PlatformNotSupportedException e) { if (e.Message.Length == 0) return 1; rejected++; }
        try { new StrongNameKeyPair((byte[])null!); } catch (PlatformNotSupportedException) { rejected++; }
        try { new StrongNameKeyPair("unused"); } catch (PlatformNotSupportedException) { rejected++; }
        try { new StrongNameKeyPair((string)null!); } catch (PlatformNotSupportedException) { rejected++; }
        try { new StrongNameKeyPair((FileStream)null!); } catch (PlatformNotSupportedException) { rejected++; }
        try { new Derived(); } catch (PlatformNotSupportedException) { rejected++; }
        if (rejected != 6 || bytes[0] != 1 || bytes[1] != 2 || bytes[2] != 3) return 2;
        return input + 1;
    }
    private sealed class Derived : StrongNameKeyPair
    {
        public Derived() : base(null!, default(StreamingContext)) { }
    }
}
