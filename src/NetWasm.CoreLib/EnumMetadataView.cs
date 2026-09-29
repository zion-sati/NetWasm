using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System;

/// <summary>
/// Read-only view of the private enum descriptor ABI, version 1. The compiler
/// owns its storage and the referenced static strings for the module lifetime.
/// This is a selected data payload, not a reflection object or an RTTI field.
/// </summary>
internal readonly unsafe struct EnumMetadataView
{
    private const int ValuesPresent = 2;
    private const int NamesPresent = 4;
    private readonly byte* _address;

    internal EnumMetadataView(nint address)
    {
        if (address == 0)
            throw new InvalidOperationException();

        _address = (byte*)address;
        if (*(int*)_address != 1 ||
            (uint)(UnderlyingTypeCode - 1) >= 9 || Count < 0)
        {
            throw new InvalidOperationException();
        }
    }

    internal int UnderlyingTypeCode => *(int*)(_address + 4);
    internal bool HasFlagsAttribute => (*(int*)(_address + 8) & 1) != 0;
    private int Count => *(int*)(_address + 12);

    internal ReadOnlySpan<ulong> Values
    {
        get
        {
            if ((*(int*)(_address + 8) & ValuesPresent) == 0)
                throw new InvalidOperationException();

            var values = *(ulong**)(_address + 16);
            if (Count != 0 && values == null)
                throw new InvalidOperationException();

            return new ReadOnlySpan<ulong>(values, Count);
        }
    }

    internal ReadOnlySpan<string> Names
    {
        get
        {
            if ((*(int*)(_address + 8) & NamesPresent) == 0)
                throw new InvalidOperationException();

            var names = *(void**)(_address + 16 + sizeof(nint));
            if (Count != 0 && names == null)
                throw new InvalidOperationException();

            return MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef<string>(names), Count);
        }
    }
}
