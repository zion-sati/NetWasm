using System;

namespace NetWasm.Hosting.Deployment;

public interface IContentHasher
{
    string Hash(ReadOnlyMemory<byte> content);
}
