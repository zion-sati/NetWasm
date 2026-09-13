using System;
using System.IO;

namespace NetWasm.Compiler.Cli;

internal interface ITextFileReader
{
    string Read(string path);
}

internal sealed class SystemTextFileReader : ITextFileReader
{
    public string Read(string path) => File.ReadAllText(path);
}

internal interface ITextFileWriter
{
    void Write(string path, string content);
}

internal sealed class SystemTextFileWriter : ITextFileWriter
{
    public void Write(string path, string content) => File.WriteAllText(path, content);
}

internal interface IBinaryFileWriter
{
    void Write(string path, byte[] content);
}

internal sealed class SystemBinaryFileWriter : IBinaryFileWriter
{
    public void Write(string path, byte[] content) => File.WriteAllBytes(path, content);
}
