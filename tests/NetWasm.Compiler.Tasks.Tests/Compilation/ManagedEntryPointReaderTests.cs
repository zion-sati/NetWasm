using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.ManagedExecutables;
using NetWasm.Compiler.Tasks.Compilation;
using NetWasm.Compiler.Tasks.Tests.Fixtures.ManagedExecutable;
using AsyncManagedExecutableMarker =
    NetWasm.Compiler.Tasks.Tests.Fixtures.AsyncManagedExecutable.Marker;
using AsyncVoidManagedExecutableMarker =
    NetWasm.Compiler.Tasks.Tests.Fixtures.AsyncVoidManagedExecutable.Marker;

namespace NetWasm.Compiler.Tasks.Tests.Compilation;

public sealed class ManagedEntryPointReaderTests
{
    [Fact]
    public void ReadReturnsThePeSelectedManagedEntryPoint()
    {
        var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(new ManagedEntryPointReader());

        var entryPoint = reader.Read(typeof(Marker).Assembly.Location);
        var values = typeof(ManagedEntryPoint)
            .GetProperties()
            .Select(property => property.GetValue(entryPoint))
            .ToArray();

        Assert.Contains(
            "Container+Program",
            values);
        Assert.Contains("Main", values);
        Assert.Contains(values, value => value is int token && token > 0);
        Assert.Equal(
            new ManagedExecutableEntryPointAbi(
                ManagedExecutableParameterShape.None,
                ManagedExecutableReturnShape.ExitCode),
            entryPoint.Abi);
    }

    [Theory]
    [InlineData("NoArgumentsVoidProgram", ManagedExecutableParameterShape.None,
        ManagedExecutableReturnShape.Void)]
    [InlineData("StringArgumentsVoidProgram", ManagedExecutableParameterShape.StringArray,
        ManagedExecutableReturnShape.Void)]
    [InlineData("StringArgumentsExitCodeProgram", ManagedExecutableParameterShape.StringArray,
        ManagedExecutableReturnShape.ExitCode)]
    public void ReadReturnsEverySupportedManagedExecutableAbi(
        string typeName,
        ManagedExecutableParameterShape parameterShape,
        ManagedExecutableReturnShape returnShape)
    {
        var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(new ManagedEntryPointReader());

        var entryPoint = ReadMutatedImage(
            reader,
            (image, header) => SelectEntryPoint(image, header, typeName));

        Assert.Equal(
            new ManagedExecutableEntryPointAbi(parameterShape, returnShape),
            entryPoint.Abi);
    }

    [Fact]
    public void ReadResolvesTheRealMethodBehindAnAsyncManagedEntryPoint()
    {
        var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(
            new ManagedEntryPointReader());

        var entryPoint = reader.Read(typeof(AsyncManagedExecutableMarker).Assembly.Location);

        Assert.Equal(
            "NetWasm.Compiler.Tasks.Tests.Fixtures.AsyncManagedExecutable.Program",
            entryPoint.TypeName);
        Assert.Equal("Main", entryPoint.MethodName);
        Assert.Equal(
            new ManagedExecutableEntryPointAbi(
                ManagedExecutableParameterShape.None,
                ManagedExecutableReturnShape.ExitCode,
                ManagedExecutableCompletionShape.Asynchronous),
            entryPoint.Abi);
        Assert.True(entryPoint.MetadataToken > 0);
    }

    [Fact]
    public void ReadResolvesTaskMainWithStringArguments()
    {
        var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(
            new ManagedEntryPointReader());

        var entryPoint = reader.Read(
            typeof(AsyncVoidManagedExecutableMarker).Assembly.Location);

        Assert.Equal("Main", entryPoint.MethodName);
        Assert.Equal(
            new ManagedExecutableEntryPointAbi(
                ManagedExecutableParameterShape.StringArray,
                ManagedExecutableReturnShape.Void,
                ManagedExecutableCompletionShape.Asynchronous),
            entryPoint.Abi);
    }

    [Theory]
    [InlineData("UnsupportedEntryPointProgram")]
    [InlineData("UnsupportedReturnProgram")]
    [InlineData("UnsupportedSingleParameterProgram")]
    [InlineData("UnsupportedArrayParameterProgram")]
    [InlineData("GenericEntryPointProgram")]
    [InlineData("InstanceEntryPointProgram")]
    public void ReadRejectsAnUnsupportedManagedExecutableAbi(string typeName)
    {
        var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(new ManagedEntryPointReader());

        Assert.Throws<CompilerException>(() => ReadMutatedImage(
            reader,
            (image, header) => SelectEntryPoint(
                image,
                header,
                typeName,
                "Invalid")));
    }

    [Fact]
    public void ReadRejectsAnAssemblyWithoutAManagedEntryPoint()
    {
        var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(new ManagedEntryPointReader());

        Assert.ThrowsAny<Exception>(() => reader.Read(typeof(IManagedEntryPointReader).Assembly.Location));
    }

    [Fact]
    public void ReadRejectsANativeEntryPointFlag()
    {
        var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(new ManagedEntryPointReader());

        Assert.Throws<CompilerException>(() => ReadMutatedImage(reader, static (image, header) =>
        {
            var flags = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(header + 16, 4));
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(header + 16, 4),
                flags | (int)CorFlags.NativeEntryPoint);
        }));
    }

    [Fact]
    public void ReadRejectsAnEntryPointThatIsNotAMethodDefinition()
    {
        var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(new ManagedEntryPointReader());

        Assert.Throws<CompilerException>(() => ReadMutatedImage(reader, static (image, header) =>
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(header + 20, 4),
                0x02000001)));
    }

    [Fact]
    public void ReadReturnsANamespacedEntryPointSelectedByThePeHeader()
    {
        var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(new ManagedEntryPointReader());

        var entryPoint = ReadMutatedImage(reader, static (image, header) =>
        {
            using var pe = new PEReader(new MemoryStream(image, writable: false));
            var metadata = pe.GetMetadataReader();
            var alternateType = metadata.TypeDefinitions
                .Select(metadata.GetTypeDefinition)
                .Single(type =>
                    metadata.GetString(type.Namespace) ==
                    "NetWasm.Compiler.Tasks.Tests.Fixtures.ManagedExecutable" &&
                    metadata.GetString(type.Name) == "AlternateProgram");
            var method = alternateType.GetMethods()
                .Single(handle => metadata.GetString(
                    metadata.GetMethodDefinition(handle).Name) == "Main");
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(header + 20, 4),
                System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(method));
        });

        Assert.Equal(
            "NetWasm.Compiler.Tasks.Tests.Fixtures.ManagedExecutable.AlternateProgram",
            entryPoint.TypeName);
        Assert.Equal("Main", entryPoint.MethodName);
    }

    [Fact]
    public void ReadRejectsAPortableExecutableWithoutAClrHeader()
    {
        var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(new ManagedEntryPointReader());

        Assert.Throws<CompilerException>(() => ReadMutatedImage(reader, static (image, _) =>
        {
            using var pe = new PEReader(new MemoryStream(image, writable: false));
            var directory = pe.PEHeaders.PEHeader!.CorHeaderTableDirectory;
            Span<byte> pattern = stackalloc byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(
                pattern,
                directory.RelativeVirtualAddress);
            BinaryPrimitives.WriteInt32LittleEndian(
                pattern[4..],
                directory.Size);
            var offset = image.AsSpan().IndexOf(pattern);
            Assert.True(offset >= 0);
            image.AsSpan(offset, pattern.Length).Clear();
        }));
    }

    [Fact]
    public void ReadRejectsMalformedPortableExecutableInput()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netwasm-invalid-pe-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, [1, 2, 3, 4]);
        try
        {
            var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(new ManagedEntryPointReader());

            Assert.Throws<CompilerException>(() => reader.Read(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ReadRejectsAnEmptyAssemblyPath(string path)
    {
        var reader = Assert.IsAssignableFrom<IManagedEntryPointReader>(new ManagedEntryPointReader());

        Assert.Throws<ArgumentException>(() => reader.Read(path));
    }

    private static ManagedEntryPoint ReadMutatedImage(
        IManagedEntryPointReader reader,
        Action<byte[], int> mutate)
    {
        var image = File.ReadAllBytes(typeof(Marker).Assembly.Location);
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        mutate(image, pe.PEHeaders.CorHeaderStartOffset);
        var path = Path.Combine(Path.GetTempPath(), $"netwasm-mutated-pe-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image);
        try
        {
            return reader.Read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void SelectEntryPoint(
        byte[] image,
        int header,
        string typeName,
        string methodName = "Main")
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var metadata = pe.GetMetadataReader();
        var type = metadata.TypeDefinitions
            .Select(metadata.GetTypeDefinition)
            .Single(candidate =>
                metadata.GetString(candidate.Namespace) ==
                "NetWasm.Compiler.Tasks.Tests.Fixtures.ManagedExecutable" &&
                metadata.GetString(candidate.Name) == typeName);
        var method = type.GetMethods()
            .Single(handle => metadata.GetString(
                metadata.GetMethodDefinition(handle).Name) == methodName);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(header + 20, 4),
            MetadataTokens.GetToken(method));
    }
}
