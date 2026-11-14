// From the compiler harness's MissingValueContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Reflection;
using System.Runtime.Serialization;
namespace MissingValueFixture;
public static class EntryPoint
{
    public static int Run(int input)
    {
        var value = Missing.Value;
        if (!object.ReferenceEquals(value, Missing.Value) || !object.ReferenceEquals(value, Type.Missing)) return 1;
        if (value.GetType() != typeof(Missing) || Type.Missing is not Missing || Type.Missing is not ISerializable) return 2;
        var caught = 0;
        try { ((ISerializable)value).GetObjectData(null, default); } catch (PlatformNotSupportedException) { caught++; }
        if (!object.ReferenceEquals(value, Type.Missing)) return 3;
        return caught == 1 ? input + 1 : 4;
    }
}
