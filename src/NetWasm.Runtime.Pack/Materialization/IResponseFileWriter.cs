using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

/// <summary>Writes a tool's arguments to a response file, one quoted argument per line.</summary>
internal interface IResponseFileWriter
{
    void Write(string path, ImmutableArray<string> arguments);
}
