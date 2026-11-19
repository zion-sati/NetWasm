// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;

namespace NetWasm.Compiler.Core.Tests.Types;

// A static method or constructor runs after its type's initialization was triggered, an instance method of a reference
// type after a constructor; the initializer and a value type's instance methods do not.
public sealed class OwnTypeInitializationTests
{
    private static readonly AssemblyIdentity Assembly = new("Owner");

    [Theory]
    [InlineData(".cctor", true, false, false)]
    [InlineData("Run", true, true, true)]
    [InlineData(".ctor", false, true, true)]
    [InlineData("Get", false, false, true)]
    public void InvocationTriggersInitializationExceptForTheInitializer(string name, bool isStatic, bool triggered, bool referenceTypeInitialized)
    {
        var method = new MethodDefinitionModel(new EntityKey(Assembly, 1), new EntityKey(Assembly, 2), name, isStatic,
            MethodSignatureModel.Create(CliValueKind.Void), 1);

        Assert.Equal(triggered, OwnTypeInitialization.TriggeredByInvocation(method));
        Assert.Equal(referenceTypeInitialized, OwnTypeInitialization.InitializedOnEntry(method, declaringTypeIsValueType: false));
        Assert.Equal(triggered, OwnTypeInitialization.InitializedOnEntry(method, declaringTypeIsValueType: true));
    }

    [Fact]
    public void RejectsAMissingMethod() =>
        Assert.Throws<ArgumentNullException>(() => OwnTypeInitialization.TriggeredByInvocation(null!));
}
