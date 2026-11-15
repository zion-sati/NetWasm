// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.IO;
using System.Text;

using NetWasm.Compiler.Diagnostics;

namespace NetWasm.Compiler.Tests;

public sealed class CompilerDiagnosticTextWriterFactoryTests
{
    [Fact]
    public void CreateMakesDirectoriesAppendsAndOmitsTheUtf8Bom()
    {
        var directory = Directory.CreateTempSubdirectory("netwasm-diagnostics-");
        var path = Path.Combine(directory.FullName, "nested", "compiler.jsonl");
        var factory = Assert.IsAssignableFrom<ICompilerDiagnosticTextWriterFactory>(
            new CompilerDiagnosticTextWriterFactory());

        try
        {
            using (var first = factory.Create(path))
            {
                first.WriteLine("first");
            }

            using (var second = factory.Create(path))
            {
                second.WriteLine("second");
            }

            Assert.Equal("first\nsecond\n", File.ReadAllText(path, Encoding.UTF8));
            Assert.False(File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void CreateRejectsABlankPath()
    {
        var factory = Assert.IsAssignableFrom<ICompilerDiagnosticTextWriterFactory>(
            new CompilerDiagnosticTextWriterFactory());

        Assert.Throws<ArgumentException>(() => factory.Create(" "));
    }
}
