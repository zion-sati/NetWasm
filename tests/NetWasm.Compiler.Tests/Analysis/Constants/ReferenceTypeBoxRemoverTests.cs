// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Analysis.Constants;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests.Analysis.Constants;

// Boxing a reference type does nothing, so generic code's `box T; brtrue` null test, once T is a class, loses its box
// and the root maps no longer count it as an allocation; a value type, a nullable or an open type keeps its box.
public sealed class ReferenceTypeBoxRemoverTests
{
    private static readonly AssemblyIdentity Assembly = new("BoxFixture");
    private static readonly CliTypeIdentity Class = CliTypeIdentity.Named(Assembly, "Fixture", "Holder", false);
    private static readonly CliTypeIdentity Struct = CliTypeIdentity.Named(Assembly, "Fixture", "Pair", true);
    // A reference by its stack kind whose definition says value type: the definition decides.
    private static readonly CliTypeIdentity Misreported = CliTypeIdentity.Named(Assembly, "Fixture", "Misreported", false);
    private static readonly CliTypeIdentity String = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference);

    public static TheoryData<string> Removed() => ["string", "class", "array"];

    [Theory]
    [MemberData(nameof(Removed))]
    public void ABoxOfAReferenceTypeGoes(string kind)
    {
        var type = kind switch
        {
            "string" => String,
            "class" => Class,
            _ => CliTypeIdentity.SzArray(Class),
        };
        var body = NullTest(type);

        var result = new ReferenceTypeBoxRemover(new Definitions()).Rewrite(body);

        Assert.Equal(CilOperation.Nop, result.Instructions[1].Operation);
        Assert.Equal(body.Instructions.Select(static instruction => instruction.Offset),
            result.Instructions.Select(static instruction => instruction.Offset));
        Assert.Equal([body.Instructions[0], body.Instructions[2], body.Instructions[3], body.Instructions[4]],
            [result.Instructions[0], result.Instructions[2], result.Instructions[3], result.Instructions[4]]);
    }

    public static TheoryData<string> Kept() => ["value type", "integer", "open", "misreported"];

    [Theory]
    [MemberData(nameof(Kept))]
    public void ABoxOfAnythingElseStays(string kind)
    {
        var type = kind switch
        {
            "value type" => Struct,
            "integer" => CliTypeIdentity.Primitive("i4", CliValueKind.I4),
            "open" => CliTypeIdentity.GenericParameter(false, 0),
            _ => Misreported,
        };
        var body = NullTest(type);

        Assert.Same(body, new ReferenceTypeBoxRemover(new Definitions()).Rewrite(body));
    }

    // ldarg 0; box T; brtrue 4; ldc 0; ret; 4: ldc 1; ret
    private static CilMethodBody NullTest(CliTypeIdentity type)
    {
        var method = new MethodDefinitionModel(new(Assembly, 0x06000001), new(Assembly, 0x02000001), "Run", true,
            MethodSignatureModel.Create(CliValueKind.I4, CliValueKind.ManagedReference), 1);
        return new(method, 2, [],
        [
            new(0, 1, CilOperation.LoadArgument, new CilOperand.Index(0)),
            new(1, 2, CilOperation.Box, new CilOperand.TypeIdentity(type)),
            new(2, 3, CilOperation.BranchIfTrue, new CilOperand.BranchTarget(5)),
            new(3, 4, CilOperation.LoadInt32, new CilOperand.ConstantI4(0)),
            new(4, 5, CilOperation.Return, new CilOperand.None()),
            new(5, 6, CilOperation.LoadInt32, new CilOperand.ConstantI4(1)),
            new(6, 7, CilOperation.Return, new CilOperand.None()),
        ]);
    }

    private sealed class Definitions : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(new(Assembly, identity.Equals(Class) ? 1 : 2), "Fixture", identity.FullName ?? "", !identity.Equals(Class), [], []);
    }
}
