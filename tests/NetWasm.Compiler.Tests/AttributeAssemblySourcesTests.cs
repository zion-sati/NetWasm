// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis.Attributes;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Tests;

// The calls that prove an assembly: RuntimeAssembly.Of<T>() (what Assembly.GetExecutingAssembly() becomes) and
// Type.Assembly on a proven type. Anything else, an open type or a type that is not a declaration proves nothing.
public sealed class AttributeAssemblySourcesTests
{
    private static readonly AssemblyIdentity Core = new("Core");
    private static readonly AssemblyIdentity User = new("User");
    private static readonly CliTypeIdentity UserType = CliTypeIdentity.Named(User, "Example", "Program", false);
    private static readonly EntityKey OfKey = new(Core, 0x06000001);
    private static readonly EntityKey AssemblyGetterKey = new(Core, 0x06000002);

    [Fact]
    public void OfAndTypeAssemblyProveTheTypesAssembly()
    {
        var sources = new AttributeAssemblySources(new Definitions(), OfKey, AssemblyGetterKey);

        var of = sources.Prove(CilOperation.Call, Method(OfKey, true, [UserType]), []);
        var getter = sources.Prove(CilOperation.CallVirtual, Method(AssemblyGetterKey, false, []),
            [new(AttributeValueKind.Type, UserType)]);

        Assert.Equal(new AttributeValueProof(AttributeValueKind.Assembly, Assembly: User), of);
        Assert.Equal(new AttributeValueProof(AttributeValueKind.Assembly, Assembly: User), getter);
    }

    [Fact]
    public void OtherCallsOpenTypesAndShapesProveNothing()
    {
        var sources = new AttributeAssemblySources(new Definitions(), OfKey, AssemblyGetterKey);
        var open = CliTypeIdentity.GenericParameter(method: true, 0);

        Assert.Null(sources.Prove(CilOperation.Call, Method(new(Core, 0x06000003), true, [UserType]), []));
        Assert.Null(sources.Prove(CilOperation.CallVirtual, Method(OfKey, true, [UserType]), []));
        Assert.Null(sources.Prove(CilOperation.Call, Method(OfKey, true, [open]), []));
        Assert.Null(sources.Prove(CilOperation.Call, Method(OfKey, true, [CliTypeIdentity.SzArray(UserType)]), []));
        Assert.Null(sources.Prove(CilOperation.CallVirtual, Method(AssemblyGetterKey, false, []), [default]));
        Assert.Null(sources.Prove(CilOperation.NewObject, Method(AssemblyGetterKey, false, []),
            [new(AttributeValueKind.Type, UserType)]));
    }

    private static MethodInstanceModel Method(EntityKey key, bool isStatic, ImmutableArray<CliTypeIdentity> arguments)
    {
        var signature = MethodSignatureModel.Create(CliValueKind.ManagedReference);
        return new(new(key, new(Core, 0x02000001), "Method", isStatic, signature, arguments.Length), UserType, arguments, signature);
    }

    private sealed class Definitions : ITypeDefinitionResolver
    {
        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) =>
            new(new(identity.Assembly!.Value, 0x02000002), "Example", "Program", false, [], []);
    }
}
