// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Core.Tests.Types;

public sealed class MethodInitializationTriggerClassifierTests
{
    private static readonly EntityKey Type = new(new AssemblyIdentity("Initialization"), 0x02000001);
    private static readonly MethodDefinitionModel Method = new(
        new EntityKey(Type.Assembly, 0x06000001), Type, "Read", false,
        MethodSignatureModel.Create(CliValueKind.Void), 1);

    [Theory]
    [InlineData(true, false, false, "Read", true)]
    [InlineData(false, false, false, ".ctor", true)]
    [InlineData(false, true, false, ".ctor", true)]
    [InlineData(false, true, false, "Read", true)]
    [InlineData(false, false, false, "Read", false)]
    [InlineData(true, false, true, "Read", false)]
    [InlineData(false, false, true, ".ctor", false)]
    [InlineData(false, true, true, "Read", false)]
    [InlineData(true, true, false, ".cctor", false)]
    public void ClassifiesOnlyRequiredMethodTriggers(
        bool isStatic, bool isValueType, bool beforeFieldInit, string name, bool expected)
    {
        var types = new Types(new(Type, "", "Owner", isValueType, [], [])
        {
            IsBeforeFieldInit = beforeFieldInit,
        });
        var classifier = Assert.IsAssignableFrom<IMethodInitializationTriggerClassifier>(
            new MethodInitializationTriggerClassifier(types));

        Assert.Equal(expected, classifier.Classify(Method with { IsStatic = isStatic, Name = name }));
        Assert.Equal(name == ".cctor" ? 0 : 1, types.Reads);
    }

    [Fact]
    public void RejectsMissingDependenciesAndInput()
    {
        Assert.Throws<ArgumentNullException>(() => new MethodInitializationTriggerClassifier(null!));
        var types = new Types(new(Type, "", "Owner", false, [], []));
        var classifier = Assert.IsAssignableFrom<IMethodInitializationTriggerClassifier>(
            new MethodInitializationTriggerClassifier(types));
        Assert.Throws<ArgumentNullException>(() => classifier.Classify(null!));
        Assert.Equal(0, types.Reads);
    }

    private sealed class Types(TypeDefinitionModel definition) : ITypeRepository
    {
        public int Reads { get; private set; }
        public TypeDefinitionModel GetTypeDefinition(EntityKey key)
        {
            Assert.Equal(Type, key);
            Reads++;
            return definition;
        }
    }
}
