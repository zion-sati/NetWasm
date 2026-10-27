using System.Buffers.Binary;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata.ManagedExecutables;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ManagedExecutableEntryPointRejectionTests
{
    private const string Program = """
        static class Program
        {
            static void Main() { }
            static void Generic<T>() { }
            static string Text() => "";
            static void Numbers(int[] values) { }
            static void Number(int value) { }
            static void Two(string[] first, string[] second) { }
        }
        sealed class Holder
        {
            void Instance() { }
        }
        """;

    // Each method fails one check of the entry ABI, which a caller selects by token.
    [Theory]
    [InlineData("Instance")]
    [InlineData("Generic")]
    [InlineData("Text")]
    [InlineData("Numbers")]
    [InlineData("Number")]
    [InlineData("Two")]
    public void RejectsASelectedMethodWithoutAnEntryAbi(string method)
    {
        var image = Compile(OutputKind.ConsoleApplication);

        AssertRejected(image, Token(image, method));
    }

    [Fact]
    public void RejectsAnImageWithoutAnEntryPoint() =>
        AssertRejected(Compile(OutputKind.DynamicallyLinkedLibrary));

    [Fact]
    public void RejectsANativeEntryPoint()
    {
        var image = Compile(OutputKind.ConsoleApplication);
        var header = CorHeaderOffset(image);
        var flags = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(header + 16, 4));
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(header + 16, 4), flags | (int)CorFlags.NativeEntryPoint);

        AssertRejected(image);
    }

    [Fact]
    public void RejectsAnImageWithoutACliHeader()
    {
        var image = Compile(OutputKind.ConsoleApplication);
        using (var pe = new PEReader(new MemoryStream(image, writable: false)))
        {
            var directory = pe.PEHeaders.PEHeader!.CorHeaderTableDirectory;
            Span<byte> entry = stackalloc byte[8];
            BinaryPrimitives.WriteInt32LittleEndian(entry, directory.RelativeVirtualAddress);
            BinaryPrimitives.WriteInt32LittleEndian(entry[4..], directory.Size);
            image.AsSpan(image.AsSpan().IndexOf(entry), entry.Length).Clear();
        }

        AssertRejected(image);
    }

    private static void AssertRejected(byte[] image, int? token = null)
    {
        var exception = Assert.Throws<CompilerException>(() =>
            new ManagedExecutableEntryPointSelectorFactory().Create().SelectEntryPoint("virtual/app.dll", image, token));

        Assert.Equal(DiagnosticCode.InvalidEntryPoint, exception.Diagnostic.Code);
    }

    private static int Token(byte[] image, string name)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var metadata = pe.GetMetadataReader();
        return MetadataTokens.GetToken(metadata.MethodDefinitions.Single(handle =>
            metadata.GetString(metadata.GetMethodDefinition(handle).Name) == name));
    }

    private static int CorHeaderOffset(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        return pe.PEHeaders.CorHeaderStartOffset;
    }

    private static byte[] Compile(OutputKind kind)
    {
        var compilation = CSharpCompilation.Create("EntryRejection", [CSharpSyntaxTree.ParseText(Program)],
            [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            new CSharpCompilationOptions(kind));
        using var output = new MemoryStream();
        var result = compilation.Emit(output);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics.Select(diagnostic => diagnostic.Id)));
        return output.ToArray();
    }
}
