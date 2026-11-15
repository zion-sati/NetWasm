// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

public sealed class AttributeQueryMethodSpecializerTests
{
    [Fact]
    public void AttributeLoweringReceivesTheSpecializedBody()
    {
        var fixture = new Fixture();
        var specializer = new AttributeQueryMethodSpecializer(fixture, fixture);
        Assert.Same(fixture.Lowered, ((IMethodSpecializer)specializer).Rewrite(fixture.Input));
        Assert.Equal(["specialize", "lower"], fixture.Calls);
    }

    [Fact]
    public void SpecializationFailurePreventsAttributeLowering()
    {
        var fixture = new Fixture { Failure = new InvalidOperationException("Specialization failed") };
        var specializer = new AttributeQueryMethodSpecializer(fixture, fixture);
        Assert.Same(fixture.Failure, Assert.Throws<InvalidOperationException>(() => ((IMethodSpecializer)specializer).Rewrite(fixture.Input)));
        Assert.Equal(["specialize"], fixture.Calls);
    }

    private sealed class Fixture : IMethodSpecializer, IAttributeQueryLowerer
    {
        internal Fixture()
        {
            var assembly = new AssemblyIdentity("Specialization");
            Input = new(new(new(assembly, 1), new(assembly, 2), "Run", true,
                MethodSignatureModel.Create(CliValueKind.Void), 1), 1, [], []);
            Specialized = Input with { MaxStack = 2 };
            Lowered = Input with { MaxStack = 3 };
        }

        internal CilMethodBody Input { get; }
        internal CilMethodBody Specialized { get; }
        internal CilMethodBody Lowered { get; }
        internal Exception? Failure { get; init; }
        internal List<string> Calls { get; } = [];

        public CilMethodBody Rewrite(CilMethodBody body)
        {
            Assert.Same(Input, body);
            Calls.Add("specialize");
            return Failure is null ? Specialized : throw Failure;
        }

        public CilMethodBody Lower(CilMethodBody body)
        {
            Assert.Same(Specialized, body);
            Calls.Add("lower");
            return Lowered;
        }
    }
}
