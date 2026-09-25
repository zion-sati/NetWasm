using System;
using System.Runtime.InteropServices.WebAssembly;

namespace NetWasm.Wit.Netwasm.Test.Canonical._1._0._0;

public static partial class TestCanonicalExports
{
    public static partial uint BoundaryProbes()
    {
        var result = 0u;
        try { CanonicalAbi.LiftBoolean(2); }
        catch (ArgumentException) { result |= 1u; }
        try { CanonicalAbi.LiftCharacter(0xd800); }
        catch (ArgumentException) { result |= 2u; }
        try { CanonicalAbi.LiftCharacter(0x110000); }
        catch (ArgumentException) { result |= 4u; }
        try { CanonicalAbi.LiftDiscriminant(-1, 3); }
        catch (ArgumentException) { result |= 8u; }
        try { CanonicalAbi.LiftFlagsWord(8, 3); }
        catch (ArgumentException) { result |= 16u; }
        if (RejectsManagedString("\ud800")) { result |= 32u; }
        if (RejectsManagedString("\udc00")) { result |= 64u; }
        if (RejectsUtf8([0xc0, 0xaf])) { result |= 128u; }
        if (RejectsUtf8([0xe2, 0x82])) { result |= 256u; }
        if (RejectsUtf8([0xed, 0xa0, 0x80])) { result |= 512u; }
        if (RejectsUtf8([0xf4, 0x90, 0x80, 0x80])) { result |= 1024u; }
        try { CanonicalAbi.AllocateElements(nuint.MaxValue, 2, 1); }
        catch (OutOfMemoryException) { result |= 2048u; }
        try { CanonicalAbi.LowerString(null!); }
        catch (ArgumentNullException) { result |= 4096u; }
        return result;
    }

    public static partial ScalarValues RoundTripScalars(ScalarValues value) =>
        HostImports.RoundTripScalars(value);

    public static partial WitResult<(uint, string), string> Transform(
        Item[] values,
        WitOption<Choice> selected,
        Permissions permissions)
    {
        var result = HostImports.Transform(values, selected, permissions);
        return result.IsOk
            ? WitResult<(uint, string), string>.FromOk(
                (checked(result.Ok.Item1 + 1), result.Ok.Item2))
            : WitResult<(uint, string), string>.FromError(result.Error);
    }

    private static bool RejectsManagedString(string value)
    {
        try
        {
            CanonicalAbi.LowerString(value);
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static bool RejectsUtf8(byte[] value)
    {
        var address = CanonicalAbi.Allocate((nuint)value.Length, 1);
        try
        {
            for (var index = 0; index < value.Length; index++)
            {
                CanonicalAbi.WriteByte(address, (nuint)index, value[index]);
            }
            try
            {
                CanonicalAbi.LiftString(address, (nuint)value.Length);
                return false;
            }
            catch (ArgumentException)
            {
                return true;
            }
        }
        finally
        {
            CanonicalAbi.Free(address);
        }
    }
}

public static class CanonicalMatrixComponent
{
    public static int Run(int value) => value;
}
