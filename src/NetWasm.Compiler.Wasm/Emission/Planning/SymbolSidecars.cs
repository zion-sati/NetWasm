using System;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

// The program's sidecars as the compile planned them: its file name, which sidecars carry names (issue 225),
// the digest of its runtime type names, and the CoreLib static word the runtime initializer fills with the
// sidecar descriptor's address (null when nothing in the program reads the descriptor).
public sealed record WasmSidecarRequest(
    string ProgramFileName,
    bool LoadTypeSymbols,
    ImmutableArray<byte> TypeSymbolsDigest,
    int? DescriptorSlotAddress);

// What CoreLib reads at the descriptor's address: flags, the build ID and the program's file name.
internal sealed record SidecarDescriptor(int SlotAddress, int Flags, string BuildId, string ProgramFileName)
{
    public const int TypeSymbolsFlag = 1;
    public const int MethodSymbolsFlag = 2;

    // i32 flags, i32 build ID length, its UTF-16 characters, i32 file name length, its UTF-16 characters.
    public byte[] Encode()
    {
        var bytes = new byte[sizeof(int) * 3 + (BuildId.Length + ProgramFileName.Length) * sizeof(char)];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, Flags);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], BuildId.Length);
        System.Text.Encoding.Unicode.GetBytes(BuildId, span[8..]);
        var name = 8 + BuildId.Length * sizeof(char);
        BinaryPrimitives.WriteInt32LittleEndian(span[name..], ProgramFileName.Length);
        System.Text.Encoding.Unicode.GetBytes(ProgramFileName, span[(name + 4)..]);
        return bytes;
    }
}

internal interface ISymbolBuildIdCalculator
{
    string Calculate(ImmutableArray<byte> typeSymbolsDigest, ImmutableArray<WasmStackTraceSymbol> methodSymbols);
}

// A build's ID binds the module to its symbols sidecars: the first 16 bytes of SHA-256 over the digest of its
// runtime type names and every method symbol, as 32 lowercase hex digits. It is a function of the names alone, so
// a build that names everything the same, such as a compile that keeps the last one's layouts and symbols,
// keeps its ID, and the same compile gives the same ID everywhere (#187).
internal sealed class SymbolBuildIdCalculator : ISymbolBuildIdCalculator
{
    public string Calculate(ImmutableArray<byte> typeSymbolsDigest, ImmutableArray<WasmStackTraceSymbol> methodSymbols)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("netwasm-symbols/1\n"u8);
        hash.AppendData(typeSymbolsDigest.IsDefault ? [] : typeSymbolsDigest.AsSpan());
        var number = new byte[sizeof(int)];
        foreach (var symbol in methodSymbols.IsDefault ? [] : methodSymbols)
        {
            BinaryPrimitives.WriteInt32LittleEndian(number, symbol.Id);
            hash.AppendData(number);
            BinaryPrimitives.WriteInt32LittleEndian(number, symbol.Name.Length);
            hash.AppendData(number);
            hash.AppendData(System.Text.Encoding.Unicode.GetBytes(symbol.Name));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset(), 0, 16);
    }
}
