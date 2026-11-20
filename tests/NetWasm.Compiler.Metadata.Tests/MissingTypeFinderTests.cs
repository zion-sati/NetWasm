// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Metadata.Tests;

// The first type an instruction names that does not resolve, its element and type arguments included, is reported
// where the instruction is; a body whose types all resolve reports nothing.
public sealed class MissingTypeFinderTests
{
    private static readonly AssemblyIdentity Library = new("Library");
    private static readonly CliTypeIdentity Present = CliTypeIdentity.Named(Library, "N", "Present", isValueType: false);
    private static readonly CliTypeIdentity Absent = CliTypeIdentity.Named(Library, "N", "Absent", isValueType: false);

    public static TheoryData<CliTypeIdentity, bool> Types => new()
    {
        { Present, false },
        { Absent, true },
        { CliTypeIdentity.SzArray(Absent), true },
        { CliTypeIdentity.GenericInstantiation(Present, [Absent]), true },
        { CliTypeIdentity.GenericInstantiation(Present, [Present]), false },
        { CliTypeIdentity.GenericInstantiation(Absent, [Present]), true },
        { CliTypeIdentity.ManagedByReference(CliTypeIdentity.UnmanagedPointer(Present)), false },
        { CliTypeIdentity.Primitive("i4", CliValueKind.I4), false },
    };

    [Theory]
    [MemberData(nameof(Types))]
    public void ATypeTokenNamingAnAbsentTypeIsReportedWhereItIs(CliTypeIdentity type, bool missing)
    {
        var body = new CilMethodBody(new MethodDefinitionModel(new(Library, 0x06000001), new(Library, 0x02000001), "Run",
                true, MethodSignatureModel.Create(CliValueKind.Void), 1), 1, [],
            [
                new(0, 1, CilOperation.Nop, new CilOperand.None()),
                new(1, 6, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(type)),
                new(6, 7, CilOperation.Pop, new CilOperand.None()),
            ]);

        var found = new MissingTypeFinder(new Definitions()).Find(body, "Library.C::Run");

        if (!missing)
        {
            Assert.Null(found);
            return;
        }
        Assert.Equal((MissingMemberKind.Type, "N.Absent", "Library", "Library.C::Run", 1),
            (found!.Kind, found.Member, found.Assembly, found.Method, found.IlOffset));
        Assert.Throws<ArgumentNullException>(() => new MissingTypeFinder(null!));
    }

    // The exact type of a GetType receiver names its type as a type token does.
    [Fact]
    public void AnExactTypeNamingAnAbsentTypeIsReported()
    {
        var body = new CilMethodBody(new MethodDefinitionModel(new(Library, 0x06000001), new(Library, 0x02000001), "Run",
                true, MethodSignatureModel.Create(CliValueKind.Void), 1), 1, [],
            [new(0, 6, CilOperation.Nop, new CilOperand.ExactType(Absent))]);

        var found = new MissingTypeFinder(new Definitions()).Find(body, "Library.C::Run");

        Assert.Equal(("N.Absent", 0), (found!.Member, found.IlOffset));
    }

    private sealed class Definitions : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => identity.FullName == "N.Absent"
            ? throw new CompilerException(new(DiagnosticCode.AssemblyResolution, "not found")
            {
                MissingMember = new(MissingMemberKind.Type, "N.Absent", "", 0, "not found") { Assembly = "Library" },
            })
            : new TypeDefinitionModel(new(Library, 0x02000002), "N", "Present", false, [], []);
    }
}
