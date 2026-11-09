namespace System.Runtime.InteropServices;

using System.Runtime.InteropServices.WebAssembly;

internal interface ICanonicalBufferAllocator
{
    CanonicalBuffer Allocate(nuint size, nuint alignment);
}
