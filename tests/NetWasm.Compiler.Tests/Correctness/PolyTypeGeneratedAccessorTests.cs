// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

namespace NetWasm.Compiler.Tests.Correctness;

public sealed class PolyTypeGeneratedAccessorTests(CorrectnessTestRunner runner)
{
    [Fact]
    public void ActualGeneratedExternsPreservePrivateAndGenericAccess()
    {
        using var stream = typeof(PolyTypeGeneratedAccessorTests).Assembly.GetManifestResourceStream(
            "Correctness/HostFixtures/PolyType/GeneratedAccessors.cs.txt");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        runner.Run(new("PolyTypeGeneratedAccessors", "PolyTypeGeneratedAccessors", reader.ReadToEnd(), [0])
        {
            ExpectedReturnValue = 255,
            Matrix = new(CorpusMatrixProfile.Extended, Backend: CorpusExecutionBackend.Linked),
        });
    }
}
