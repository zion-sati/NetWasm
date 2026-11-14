// From the compiler harness's RuntimeMemberContractCompilationTests.cs: the same program, compiled once with the suite. On desktop .NET 11 it
// builds with DESKTOP_ORACLE defined.
#nullable disable
#pragma warning disable
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security;
namespace RuntimeMemberContractFixture;

struct Plain { public int Value; }

sealed class SignedMissingMember : MissingMemberException
{
    public SignedMissingMember(byte[] signature) => Signature = signature;
    public byte[]? Read() => Signature;
}

public static class EntryPoint
{
    static int calls;
    static void First() => calls += 1;
    static void Second() => calls += 10;

    public static int Run(int input)
    {
        // Default handles have value zero and compare by identity.
        RuntimeMethodHandle method = default, otherMethod = default;
        RuntimeFieldHandle field = default, otherField = default;
        if (method.Value != IntPtr.Zero || !method.Equals(otherMethod) || !method.Equals((object)otherMethod) || method != otherMethod || method.GetHashCode() != otherMethod.GetHashCode()) return 1;
        if (field.Value != IntPtr.Zero || !field.Equals(otherField) || !field.Equals((object)otherField) || field != otherField || field.GetHashCode() != otherField.GetHashCode()) return 2;
        if (method.Equals((object)field) || field.Equals(null)) return 3;

        // Preparing an uninitialized method handle is rejected.
        try { RuntimeHelpers.PrepareMethod(method); return 4; }
        catch (ArgumentException e) when (e.ParamName == "method") { }
        try { RuntimeHelpers.PrepareMethod(method, null); return 5; }
        catch (ArgumentException e) when (e.ParamName == "method") { }

        // SecurityException keeps the method and assembly it is given.
        var security = new SecurityException("denied");
        if (security.Method != null || security.FailedAssemblyInfo != null) return 6;
        security.Method = null;
        security.FailedAssemblyInfo = null;
        if (security.Method != null || security.FailedAssemblyInfo != null || security.Message != "denied") return 7;

        // MissingMemberException exposes its passive signature field to derived types.
        var signature = new byte[] { 1, 2, 3 };
        if (new SignedMissingMember(signature).Read() != signature) return 8;

        // ValueType.ToString names the boxed value's type, as Object.ToString does.
        // A type's text is its full name (issue 225: without type names a type's name reads its ID).
        if (typeof(Plain).ToString() != typeof(Plain).FullName) return 9;
        if (((ValueType)new Plain { Value = 5 }).ToString() != typeof(Plain).FullName) return 12;

        // Delegate composition still runs through MulticastDelegate's sealed overrides.
        Action first = First, second = Second;
        var both = (Action)Delegate.Combine(first, second)!;
        both();
        if (calls != 11 || both.GetInvocationList().Length != 2) return 10;
        var remaining = (Action)Delegate.Remove(both, first)!;
        remaining();
        if (calls != 21 || remaining.GetInvocationList().Length != 1 || Delegate.Remove(remaining, second) != null) return 11;
        return input + 1;
    }
}
