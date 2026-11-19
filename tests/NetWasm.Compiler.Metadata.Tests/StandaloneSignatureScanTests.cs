// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

// Reading an assembly scans its standalone signatures for unmanaged calli signatures that take runtime
// marshalling. The table also holds field signatures, which compilers leave there for the constants a Windows PDB
// describes (Microsoft.ApplicationInsights 2.23.0 has three) and which System.Reflection.Metadata's
// StandaloneSignature.GetKind refuses: such an assembly must read, and its calli signatures still get their stubs.
public sealed class StandaloneSignatureScanTests
{
    [Fact]
    public void AFieldSignatureInTheTableIsSkippedAndCalliSignaturesStillGetStubs()
    {
        var image = CompileImage();
        // Probe.Locals' locals signature becomes a field signature: its LOCAL_SIG header byte becomes FIELD, so the
        // row reads as a field of its first local's type, as the constants of a Windows PDB do.
        ReplaceUnique(image, [.. LocalsSignature(image)], 1, 0x06);

        using var assembly = ManagedAssembly.Parse(
            "field-signature.dll",
            image,
            AssemblyIdentityAliases.Empty,
            new ValueTypeDefinitionStackKindResolver(), new NativeImportDeclarationReader());

        var rows = Enumerable.Range(1, assembly.Reader.GetTableRowCount(TableIndex.StandAloneSig))
            .Select(row => MetadataTokens.StandaloneSignatureHandle(row))
            .Select(handle => (Token: MetadataTokens.GetToken(handle),
                Header: assembly.Reader.GetBlobReader(assembly.Reader.GetStandaloneSignature(handle).Signature).ReadSignatureHeader()))
            .ToArray();
        var field = Assert.Single(rows, row => row.Header.Kind == SignatureKind.Field);
        Assert.Null(assembly.FunctionPointerStub(field.Token));
        var calli = Assert.Single(rows, row => row.Header.CallingConvention == SignatureCallingConvention.Unmanaged);
        Assert.NotNull(assembly.FunctionPointerStub(calli.Token));
    }

    private static byte[] CompileImage()
    {
        using var assets = TestAssets.Create();
        var path = assets.CompileUnsafeSource(
            "StandaloneSignatureScan",
            """
            public static unsafe class Probe
            {
                public static decimal Locals(decimal value, System.DateTime when)
                {
                    decimal total = 0;
                    System.DateTime day = when;
                    for (var i = 0; i < 3; i++)
                    {
                        total += value * i;
                        day = day.AddDays(i);
                    }
                    return total + day.Day;
                }

                public static int Calli(delegate* unmanaged<bool, int> target) => target(true);
            }
            """);
        return File.ReadAllBytes(path);
    }

    // Probe.Locals' locals signature as the blob heap stores it: its length, then the signature.
    private static byte[] LocalsSignature(byte[] image)
    {
        using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(image));
        var reader = pe.GetMetadataReader();
        var method = reader.MethodDefinitions.Select(reader.GetMethodDefinition)
            .Single(definition => reader.GetString(definition.Name) == "Locals");
        var body = pe.GetMethodBody(method.RelativeVirtualAddress);
        var signature = reader.GetBlobBytes(reader.GetStandaloneSignature(body.LocalSignature).Signature);
        Assert.Equal(0x07, signature[0]);
        return [(byte)signature.Length, .. signature];
    }

    private static void ReplaceUnique(byte[] image, byte[] pattern, int relativeOffset, byte replacement)
    {
        var match = -1;
        for (var offset = 0; offset <= image.Length - pattern.Length; offset++)
        {
            if (!image.AsSpan(offset, pattern.Length).SequenceEqual(pattern))
                continue;
            Assert.Equal(-1, match);
            match = offset;
        }
        Assert.NotEqual(-1, match);
        image[match + relativeOffset] = replacement;
    }
}
