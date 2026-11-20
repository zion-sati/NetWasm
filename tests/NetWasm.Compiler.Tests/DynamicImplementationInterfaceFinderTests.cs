// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Analysis.DynamicCasts;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests;

// The implementation interfaces a call through an interface may dispatch to: each attributed interface that derives
// from it, a generic one instantiated so that an interface it implements is the call's. Impl implements I; Other is
// attributed but implements nothing asked; Plain is not attributed. The generic ones implement, over their parameters
// T (and U): JG IG<T>; JPair IG<T>, leaving U unbound; JArr IG<T[]>; JGrid IG<T[,]>; JList IG<IList<T>>;
// JMixed IPair<int, T>; JSame IPair<T, T>; JTwo IPair<T, T> and IG<T>.
public sealed class DynamicImplementationInterfaceFinderTests
{
    private static readonly AssemblyIdentity Assembly = new("Castables");
    private static readonly CliTypeIdentity T = CliTypeIdentity.GenericParameter(false, 0);
    private static readonly CliTypeIdentity Int32 = CliTypeIdentity.Primitive("i4", CliValueKind.I4);
    private static readonly CliTypeIdentity String = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);
    private static readonly CliTypeIdentity I = Named("I");
    private static readonly CliTypeIdentity Impl = Named("Impl");
    private static readonly CliTypeIdentity Other = Named("Other");
    private static readonly CliTypeIdentity G = Named("IG`1");
    private static readonly CliTypeIdentity Pair = Named("IPair`2");
    private static readonly CliTypeIdentity List = Named("IList`1");
    private static readonly CliTypeIdentity Collection = Named("ICollection`1");
    private static readonly CliTypeIdentity JG = Named("JG`1");
    private static readonly CliTypeIdentity JMixed = Named("JMixed`1");
    private static readonly CliTypeIdentity JSame = Named("JSame`1");
    private static readonly CliTypeIdentity JArr = Named("JArr`1");
    private static readonly CliTypeIdentity JTwo = Named("JTwo`1");

    private static readonly Dictionary<CliTypeIdentity, (int Arity, CliTypeIdentity[] Implemented)> Attributed = new()
    {
        [Impl] = (0, []),
        [Other] = (0, []),
        [JG] = (1, [Of(G, T)]),
        [Named("JPair`2")] = (2, [Of(G, T)]),
        [JArr] = (1, [Of(G, CliTypeIdentity.SzArray(T))]),
        [Named("JGrid`1")] = (1, [Of(G, CliTypeIdentity.Array(T, 2))]),
        [Named("JList`1")] = (1, [Of(G, Of(List, T))]),
        [JMixed] = (1, [Of(Pair, Int32, T)]),
        [JSame] = (1, [Of(Pair, T, T)]),
        [JTwo] = (1, [Of(Pair, T, T), Of(G, T)]),
    };

    public static TheoryData<CliTypeIdentity, CliTypeIdentity[]> Calls => new()
    {
        { I, [Impl] },
        // JPair leaves U unbound; JArr, JGrid and JList implement no IG<int>.
        { Of(G, Int32), [Of(JG, Int32), Of(JTwo, Int32)] },
        { Of(G, CliTypeIdentity.SzArray(String)), [Of(JArr, String), Of(JG, CliTypeIdentity.SzArray(String)),
            Of(JTwo, CliTypeIdentity.SzArray(String))] },
        // Another rank is another array.
        { Of(G, CliTypeIdentity.Array(Int32, 3)), [Of(JG, CliTypeIdentity.Array(Int32, 3)), Of(JTwo, CliTypeIdentity.Array(Int32, 3))] },
        { Of(G, Of(Collection, Int32)), [Of(JG, Of(Collection, Int32)), Of(JTwo, Of(Collection, Int32))] },
        // JSame needs one type twice; JTwo's IG is no IPair.
        { Of(Pair, Int32, String), [Of(JMixed, String)] },
        { Of(Pair, Int32, Int32), [Of(JMixed, Int32), Of(JSame, Int32), Of(JTwo, Int32)] },
    };

    [Theory]
    [MemberData(nameof(Calls))]
    public void FindsTheAttributedInterfacesThatDeriveFromTheCallsInterface(CliTypeIdentity call, CliTypeIdentity[] expected) =>
        Assert.Equal(expected, Finder().Find(call));

    // The compilation's interfaces are read for the attribute once, whatever the calls.
    [Fact]
    public void TheAttributedInterfacesAreReadOnce()
    {
        var fakes = new Fakes();
        var finder = new DynamicImplementationInterfaceFinder(Metadata(), fakes, fakes, fakes, fakes);

        Assert.Equal([Impl], finder.Find(I));
        Assert.Equal([Of(JG, Int32), Of(JTwo, Int32)], finder.Find(Of(G, Int32)));

        Assert.Equal(Attributed.Count + 2, fakes.Reads);
    }

    [Fact]
    public void RejectsMissingCollaborators()
    {
        var fakes = new Fakes();
        var metadata = Metadata();
        Assert.Throws<ArgumentNullException>(() => new DynamicImplementationInterfaceFinder(null!, fakes, fakes, fakes, fakes));
        Assert.Throws<ArgumentNullException>(() => new DynamicImplementationInterfaceFinder(metadata, null!, fakes, fakes, fakes));
        Assert.Throws<ArgumentNullException>(() => new DynamicImplementationInterfaceFinder(metadata, fakes, null!, fakes, fakes));
        Assert.Throws<ArgumentNullException>(() => new DynamicImplementationInterfaceFinder(metadata, fakes, fakes, null!, fakes));
        Assert.Throws<ArgumentNullException>(() => new DynamicImplementationInterfaceFinder(metadata, fakes, fakes, fakes, null!));
        Assert.Throws<ArgumentNullException>(() => Finder().Find(null!));
    }

    private static DynamicImplementationInterfaceFinder Finder()
    {
        var fakes = new Fakes();
        return new(Metadata(), fakes, fakes, fakes, fakes);
    }

    // Every attributed interface, I, and Plain, which is not attributed, by their keys (the identity's index).
    private static MetadataCompilationSnapshot Metadata()
    {
        var types = Attributed.Keys.Append(I).Append(Named("Plain")).Select((type, index) =>
            new TypeDefinitionModel(new EntityKey(Assembly, 0x02000001 + index), "Castables", type.FullName!, false, [], [])
            {
                IsInterface = true,
                GenericArity = Attributed.TryGetValue(type, out var attributed) ? attributed.Arity : 0,
            });
        return new MetadataCompilationSnapshot([], Assembly, [.. types], [], [], ImmutableDictionary<string, string>.Empty);
    }

    private static CliTypeIdentity Named(string name) => CliTypeIdentity.Named(Assembly, "Castables", name, false);

    private static CliTypeIdentity Of(CliTypeIdentity definition, params CliTypeIdentity[] arguments) =>
        CliTypeIdentity.GenericInstantiation(definition, [.. arguments]);

    private sealed class Fakes : ITypeIdentityResolver, IImplementedInterfaceResolver, ICustomAttributeDescriptorReader,
        ITypeRelationshipClassifier
    {
        private static readonly CliTypeIdentity[] Keys = [.. Attributed.Keys.Append(I).Append(Named("Plain"))];

        public CliTypeIdentity GetTypeIdentity(EntityKey type) => Keys[type.MetadataToken - 0x02000001];

        public ImmutableArray<CliTypeIdentity> GetInterfaces(CliTypeIdentity type) => [.. Attributed[type.ElementType!].Implemented];

        public int Reads { get; private set; }

        public ImmutableArray<CustomAttributeDescriptor> Read(CliTypeIdentity type)
        {
            Reads++;
            return Attributed.ContainsKey(type)
                ? [new(Assembly, 0x0C000001, 0x0A000001,
                    CliTypeIdentity.Named(Assembly, "System.Runtime.InteropServices", "DynamicInterfaceCastableImplementationAttribute", false),
                    CliGenericContext.Empty)]
                : [];
        }

        // Impl derives from I only, Other from nothing, and each instantiation from the call's interface.
        public TypeRelationship Classify(CliTypeIdentity candidate, CliTypeIdentity target) =>
            new(candidate.Equals(Other) || candidate.Equals(Impl) && !target.Equals(I)
                ? TypeRelationshipCharacteristics.None
                : TypeRelationshipCharacteristics.Hierarchy);
    }
}
