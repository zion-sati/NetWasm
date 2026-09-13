using System;
namespace NetWasm.Compiler.Metadata;

public interface IMetadataCompilationLease : IDisposable
{
    MetadataCompilationSnapshot Snapshot { get; }
}
