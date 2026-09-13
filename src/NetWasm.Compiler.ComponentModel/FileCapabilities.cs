using System.IO;

namespace NetWasm.Compiler.ComponentModel;

public interface IFileExistence
{
    bool Exists(string path);
}

public sealed class SystemFileExistence : IFileExistence
{
    public bool Exists(string path) => File.Exists(path);
}

public interface IDirectoryExistence
{
    bool Exists(string path);
}

public sealed class SystemDirectoryExistence : IDirectoryExistence
{
    public bool Exists(string path) => Directory.Exists(path);
}

public interface IByteFileReader
{
    byte[] Read(string path);
}

public sealed class SystemByteFileReader : IByteFileReader
{
    public byte[] Read(string path) => File.ReadAllBytes(path);
}

public interface IByteFileWriter
{
    void Write(string path, byte[] content);
}

public sealed class SystemByteFileWriter : IByteFileWriter
{
    public void Write(string path, byte[] content) => File.WriteAllBytes(path, content);
}

public interface ITextFileWriter
{
    void Write(string path, string content);
}

public sealed class SystemTextFileWriter : ITextFileWriter
{
    public void Write(string path, string content) => File.WriteAllText(path, content);
}

public interface IFileDeleter
{
    void Delete(string path);
}

public sealed class SystemFileDeleter : IFileDeleter
{
    public void Delete(string path) => File.Delete(path);
}

public interface IDirectoryCreator
{
    void Create(string path);
}

public sealed class SystemDirectoryCreator : IDirectoryCreator
{
    public void Create(string path) => Directory.CreateDirectory(path);
}

public interface IDirectoryDeleter
{
    void Delete(string path);
}

public sealed class SystemDirectoryDeleter : IDirectoryDeleter
{
    public void Delete(string path) => Directory.Delete(path, recursive: true);
}

public interface IFileMover
{
    void Move(string sourcePath, string destinationPath);
}

public sealed class SystemFileMover : IFileMover
{
    public void Move(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, overwrite: true);
}
