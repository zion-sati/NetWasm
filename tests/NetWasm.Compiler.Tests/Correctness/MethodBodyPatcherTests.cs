// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Buffers.Binary;
using Microsoft.Extensions.DependencyInjection;

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class MethodBodyPatcherTests
{
    [Fact]
    public void PatchRemovesUnusedReservedExceptionClausesAndShrinksUsedClauses()
    {
        using var services = CorrectnessTestAssets.CreateServices();
        var compiler = services.GetRequiredService<IRoslynCorpusCompiler>();
        var fixtures = services.GetRequiredService<IRandomCilInteractionFixtureFactory>();
        var matrix = services.GetRequiredService<IRandomCilInteractionMatrix>();
        var generator = services.GetRequiredService<IRandomCilInteractionGenerator>();
        var tokenResolver = services.GetRequiredService<IRandomCilMetadataTokenResolver>();
        var serializer = services.GetRequiredService<IGeneratedCilSerializer>();
        var validator = services.GetRequiredService<IGeneratedCilValidator>();
        var patcher = services.GetRequiredService<IMethodBodyPatcher>();
        var directory = Path.Combine(
            Path.GetTempPath(),
            "netwasm-qualification",
            "method-body-patcher",
            Guid.NewGuid().ToString("N"));

        try
        {
            var compilation = compiler.Compile(
                fixtures.CreateTemplate(CilProfile.Debug),
                CilProfile.Debug,
                directory);
            var pristine = compilation.Desktop.AssemblyPath;
            var tokens = tokenResolver.Resolve(pristine);
            var allCases = matrix.GenerateExhaustive();

            var noEhPath = Copy(pristine, directory, "no-eh.dll");
            Patch(noEhPath, allCases.First(testCase => testCase.Levels["eh"] == "none"));
            var noEhLocation = PortableExecutableMethodLocator.Locate(
                File.ReadAllBytes(noEhPath),
                fixtures.CreateTemplate(CilProfile.Debug).EntryType,
                "Case000");
            var noEhImage = File.ReadAllBytes(noEhPath);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(
                noEhImage.AsSpan(noEhLocation.HeaderOffset, sizeof(ushort)));
            Assert.Equal(0, flags & 0x0008);
            Assert.Null(noEhLocation.ExceptionSectionOffset);

            var finallyPath = Copy(pristine, directory, "finally.dll");
            Patch(finallyPath, allCases.First(testCase => testCase.Levels["eh"] == "finally"));
            var finallyLocation = PortableExecutableMethodLocator.Locate(
                File.ReadAllBytes(finallyPath),
                fixtures.CreateTemplate(CilProfile.Debug).EntryType,
                "Case000");
            Assert.NotNull(finallyLocation.ExceptionSectionOffset);
            var clauseSize = finallyLocation.HasFatExceptionClauses ? 24 : 12;
            Assert.Equal(1, (finallyLocation.ExceptionSectionSize - 4) / clauseSize);

            void Patch(string path, RandomCilInteractionCase testCase)
            {
                var program = generator.Generate(testCase, tokens);
                var serialized = serializer.SerializeMethod(program);
                if (testCase.Levels["eh"] == "none")
                {
                    Assert.Empty(serialized.ExceptionRegions);
                }
                patcher.Patch(
                    path,
                    fixtures.CreateTemplate(CilProfile.Debug).EntryType,
                    "Case000",
                    serialized,
                    validator.MeasureMaximumStackDepth(program));
            }
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string Copy(string source, string directory, string name)
    {
        var destination = Path.Combine(directory, name);
        File.Copy(source, destination);
        return destination;
    }
}
