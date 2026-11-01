using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.NativeInterop;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ManagedAssemblyContractTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ParsePreservesInliningFlagsForOrdinaryAndGenericMethods(bool aggressive, bool never)
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("InlineFlags"), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("InlineFlags");
        var owner = module.DefineType("Fixture.Methods", TypeAttributes.Public);
        var flags = MethodImplAttributes.IL |
            (aggressive ? MethodImplAttributes.AggressiveInlining : 0) |
            (never ? MethodImplAttributes.NoInlining : 0);
        foreach (var name in new[] { "Ordinary", "Generic" })
        {
            var method = owner.DefineMethod(name, MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
            if (name == "Generic")
                method.DefineGenericParameters("T");
            method.SetImplementationFlags(flags);
            method.GetILGenerator().Emit(OpCodes.Ret);
        }
        _ = owner.CreateType();
        using var image = new MemoryStream();
        builder.Save(image);
        using var parsed = ManagedAssembly.Parse("memory/inline.dll", image.ToArray(),
            AssemblyIdentityAliases.Empty, new StackKinds(), new NativeFacts());

        foreach (var name in new[] { "Ordinary", "Generic" })
        {
            var method = Assert.Single(parsed.Methods.Values, value => value.Name == name);
            Assert.Equal(aggressive, method.IsAggressiveInlining);
            Assert.Equal(never, method.IsNoInlining);
        }
    }

    [Fact]
    public void ParsePreservesMembersLayoutsNamesInterfacesAndTheImageLifetime()
    {
        var builder = new PersistedAssemblyBuilder(new AssemblyName("Contracts"), typeof(object).Assembly);
        var module = builder.DefineDynamicModule("Contracts");
        var genericInterface = module.DefineType("Fixture.ICases`3", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        var parameters = genericInterface.DefineGenericParameters("Invariant", "Out", "In");
        parameters[1].SetGenericParameterAttributes(GenericParameterAttributes.Covariant);
        parameters[2].SetGenericParameterAttributes(GenericParameterAttributes.Contravariant);
        _ = genericInterface.CreateType();
        var owner = module.DefineType("Fixture.Owner`1", TypeAttributes.Public | TypeAttributes.BeforeFieldInit, typeof(List<int>));
        var parameter = owner.DefineGenericParameters("T")[0];
        owner.AddInterfaceImplementation(typeof(IDisposable));
        owner.DefineField("Readonly", typeof(int), FieldAttributes.Public | FieldAttributes.InitOnly);
        owner.DefineField("Mutable", parameter, FieldAttributes.Public);
        var getter = Method(owner, "get_Value", MethodAttributes.Public, parameter, []);
        var setter = Method(owner, "set_Value", MethodAttributes.Private, typeof(void), [parameter]);
        var property = owner.DefineProperty("Value", PropertyAttributes.None, parameter, []);
        property.SetGetMethod(getter);
        property.SetSetMethod(setter);
        var readOnly = owner.DefineProperty("ReadOnly", PropertyAttributes.None, parameter, []);
        readOnly.SetGetMethod(getter);
        var writeOnly = owner.DefineProperty("WriteOnly", PropertyAttributes.None, parameter, []);
        writeOnly.SetSetMethod(setter);
        var genericMethod = owner.DefineMethod("Generic", MethodAttributes.Public | MethodAttributes.Static, typeof(void), []);
        genericMethod.DefineGenericParameters("U");
        genericMethod.GetILGenerator().Emit(OpCodes.Ret);
        var dispose = Method(owner, "Dispose", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.Final,
            typeof(void), []);
        owner.DefineMethodOverride(dispose, typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!);
        var nested = owner.DefineNestedType("Nested", TypeAttributes.NestedPublic);
        _ = nested.CreateType();
        _ = owner.CreateType();
        var union = module.DefineType("Fixture.Union", TypeAttributes.Public | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed,
            typeof(ValueType), PackingSize.Size1, 8);
        union.DefineField("First", typeof(int), FieldAttributes.Public).SetOffset(0);
        union.DefineField("Second", typeof(short), FieldAttributes.Public).SetOffset(2);
        _ = union.CreateType();
        _ = module.DefineType("Fixture.Sequential", TypeAttributes.Public | TypeAttributes.SequentialLayout | TypeAttributes.Sealed,
            typeof(ValueType)).CreateType();
        var localBase = module.DefineType("System.ValueType", TypeAttributes.Public | TypeAttributes.Abstract, typeof(object)).CreateType();
        _ = module.DefineType("System.Int32", TypeAttributes.Public | TypeAttributes.Sealed, localBase).CreateType();
        _ = module.DefineType("Enum", TypeAttributes.Public).CreateType();
        var abstractType = module.DefineType("Fixture.Abstract", TypeAttributes.Public | TypeAttributes.Abstract);
        abstractType.DefineMethod("Abstract", MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Abstract, typeof(void), []);
        _ = abstractType.CreateType();
        using var image = new MemoryStream();
        builder.Save(image);
        var bytes = image.ToArray();
        var native = new NativeFacts();
        var stackKinds = new StackKinds();

        var parsed = ManagedAssembly.Parse("memory/contracts.dll", bytes, AssemblyIdentityAliases.Empty, stackKinds, native);
        using (parsed)
        {
            Assert.Equal("Contracts", parsed.Identity.Name);
            Assert.Equal(parsed.References.Order(StringComparer.Ordinal), parsed.References);
            Assert.NotEmpty(parsed.References);
            Assert.Equal(parsed.Methods.Count, native.Calls);
            Assert.Equal(parsed.Types.Count, stackKinds.Calls);
            Assert.All(parsed.Methods.Values, method => Assert.Same(native.Declaration, method.NativeImport));
            var parsedOwner = Assert.Single(parsed.Types.Values, type => type.FullName == "Fixture.Owner`1");
            Assert.Equal(["T"], parsedOwner.GenericParameterNames.ToArray());
            Assert.True(parsedOwner.IsBeforeFieldInit);
            Assert.False(parsedOwner.IsValueType);
            Assert.False(parsed.Metadata.BaseTypes[parsedOwner.Key.MetadataToken].IsNil);
            Assert.Single(parsed.Metadata.ImplementedInterfaces[parsedOwner.Key.MetadataToken]);
            Assert.Contains(parsed.Types.Values, type => type.FullName == "Fixture.Owner`1+Nested");
            Assert.True(Assert.Single(parsed.Types.Values, type => type.FullName == "System.Int32").IsValueType);
            Assert.False(Assert.Single(parsed.Types.Values, type => type.FullName == "System.ValueType").IsValueType);
            Assert.False(Assert.Single(parsed.Types.Values, type => type.FullName == "Enum").IsValueType);
            var cases = Assert.Single(parsed.Types.Values, type => type.Name == "ICases`3");
            Assert.True(cases.IsInterface);
            Assert.True(cases.IsAbstract);
            Assert.Equal(3, cases.GenericArity);
            Assert.Equal([CliGenericVariance.Invariant, CliGenericVariance.Covariant, CliGenericVariance.Contravariant], cases.GenericParameterVariances.ToArray());
            var parsedUnion = Assert.Single(parsed.Types.Values, type => type.Name == "Union");
            Assert.Equal(CliTypeLayoutKind.Explicit, parsedUnion.LayoutKind);
            Assert.Equal(1, parsedUnion.PackingSize);
            Assert.Equal(8, parsedUnion.DeclaredSize);
            Assert.True(parsedUnion.IsSealed);
            Assert.Equal(0, Assert.Single(parsed.Fields.Values, field => field.Name == "First").ExplicitOffset);
            Assert.Equal(2, Assert.Single(parsed.Fields.Values, field => field.Name == "Second").ExplicitOffset);
            Assert.Equal(CliTypeLayoutKind.Sequential, Assert.Single(parsed.Types.Values, type => type.Name == "Sequential").LayoutKind);
            Assert.True(Assert.Single(parsed.Fields.Values, field => field.Name == "Readonly").IsInitOnly);
            Assert.False(Assert.Single(parsed.Fields.Values, field => field.Name == "Mutable").IsInitOnly);
            var parsedProperty = Assert.Single(parsed.Properties.Values, candidate => candidate.Name == "Value");
            Assert.Equal(parsedOwner.Key, parsedProperty.DeclaringType);
            Assert.Equal("!0", parsedProperty.PropertyType.CanonicalName);
            Assert.Empty(parsedProperty.IndexParameterTypes);
            Assert.True(parsed.Methods[parsedProperty.Getter!.Value.MetadataToken].IsPublic);
            Assert.False(parsed.Methods[parsedProperty.Setter!.Value.MetadataToken].IsPublic);
            Assert.Null(Assert.Single(parsed.Properties.Values, candidate => candidate.Name == "ReadOnly").Setter);
            Assert.Null(Assert.Single(parsed.Properties.Values, candidate => candidate.Name == "WriteOnly").Getter);
            Assert.Equal(1, Assert.Single(parsed.Methods.Values, method => method.Name == "Generic").GenericArity);
            var parsedDispose = Assert.Single(parsed.Methods.Values, method => method.Name == "Dispose");
            Assert.True(parsedDispose.IsVirtual);
            Assert.True(parsedDispose.IsNewSlot);
            Assert.True(parsedDispose.IsFinal);
            Assert.True(parsedDispose.HasBody);
            Assert.True(Assert.Single(parsed.Methods.Values, method => method.Name == "Abstract").IsAbstract);
            Assert.NotNull(parsed.PortableExecutableReader);
        }
        Assert.Throws<ObjectDisposedException>(() => parsed.PortableExecutableReader);
        parsed.Dispose();
    }

    [Fact]
    public void ParseReadsPrimitiveAndDeclaredSizeInitializersWithoutReadingFiles()
    {
        var fixture = new ImageFixture();
        var primitive = fixture.Metadata.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.HasFieldRVA,
            fixture.String("Primitive"), fixture.FieldSignature(PrimitiveTypeCode.Int32));
        fixture.Metadata.AddFieldRelativeVirtualAddress(primitive, 0);
        var storage = fixture.Metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed,
            fixture.String("Fixture"), fixture.String("Storage"), fixture.TypeReference("System", "ValueType"),
            MetadataTokens.FieldDefinitionHandle(3), MetadataTokens.MethodDefinitionHandle(2));
        fixture.Metadata.AddTypeLayout(storage, 1, 3);
        var signature = new BlobBuilder();
        new BlobEncoder(signature).FieldSignature().Type(storage, isValueType: true);
        var aggregate = fixture.Metadata.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.HasFieldRVA,
            fixture.String("Aggregate"), fixture.Metadata.GetOrAddBlob(signature));
        fixture.Metadata.AddFieldRelativeVirtualAddress(aggregate, 4);
        var data = new BlobBuilder();
        data.WriteBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        using var parsed = Parse(fixture.Image(data));

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, Assert.Single(parsed.Fields.Values, field => field.Name == "Primitive").InitialData.ToArray());
        Assert.Equal(new byte[] { 5, 6, 7 }, Assert.Single(parsed.Fields.Values, field => field.Name == "Aggregate").InitialData.ToArray());
    }

    [Theory]
    [InlineData("JSImport", 1)]
    [InlineData("JSImport", 2)]
    [InlineData("JSImportPromise", 2)]
    [InlineData("JSExport", 0)]
    [InlineData("JSExport", 1)]
    [InlineData("WitImport", 2)]
    [InlineData("WitExport", 2)]
    [InlineData("WitPostReturn", 2)]
    public void ParsePreservesEveryHostDeclarationAndIgnoresUnrelatedAttributes(string kind, int strings)
    {
        var fixture = new ImageFixture();
        fixture.Attribute(fixture.Method, "Unrelated", "Marker", []);
        var names = strings switch { 0 => Array.Empty<string?>(), 1 => ["call"], _ => new string?[] { "scope", "call" } };
        fixture.Attribute(fixture.Method, kind.StartsWith("Wit", StringComparison.Ordinal) ? "System.Runtime.InteropServices.WebAssembly" : "System.Runtime.InteropServices.JavaScript",
            kind + "Attribute", names);

        using var parsed = Parse(fixture.Image());

        var method = Assert.Single(parsed.Methods.Values);
        Assert.Equal("Run", method.Name);
        Assert.False(method.HasBody);
        if (kind.StartsWith("JSImport", StringComparison.Ordinal))
        {
            Assert.Equal(names[0], method.JSImport!.FunctionName);
            Assert.Equal(strings == 1 ? null : names[1], method.JSImport.ModuleName);
            Assert.Equal(kind == "JSImportPromise", method.JSImport.IsPromise);
        }
        else if (kind == "JSExport")
            Assert.Equal(strings == 0 ? null : names[0], method.JSExport!.ExportName);
        else if (kind == "WitImport")
            Assert.Equal(new WitImportDeclaration("scope", "call"), method.WitImport);
        else if (kind == "WitExport")
            Assert.Equal(new WitExportDeclaration("scope", "call"), method.WitExport);
        else
            Assert.Equal(new WitPostReturnDeclaration("scope", "call"), method.WitPostReturn);
    }

    [Theory]
    [InlineData("JSImport", 0)]
    [InlineData("JSImport", 1)]
    [InlineData("JSImport", 2)]
    [InlineData("JSImport", 3)]
    [InlineData("JSExport", 0)]
    [InlineData("JSExport", 1)]
    [InlineData("JSExport", 2)]
    [InlineData("JSExport", 3)]
    [InlineData("WitImport", 0)]
    [InlineData("WitImport", 1)]
    [InlineData("WitImport", 2)]
    [InlineData("WitImport", 3)]
    [InlineData("WitImport", 4)]
    public void ParseRejectsInvalidHostDeclarationPayloads(string kind, int shape)
    {
        var fixture = new ImageFixture();
        string?[] names = kind.StartsWith("Wit", StringComparison.Ordinal) ? ["scope", "call"] : ["call"];
        if (kind == "JSImport" && shape == 3)
            names = ["call", "scope"];
        if (shape == 1)
            names[^1] = " ";
        if (shape == 4)
            names[0] = null;
        fixture.Attribute(fixture.Method, kind.StartsWith("Wit", StringComparison.Ordinal) ? "System.Runtime.InteropServices.WebAssembly" : "System.Runtime.InteropServices.JavaScript",
            kind + "Attribute", names, prolog: shape == 0 ? (ushort)0 : (ushort)1,
            named: shape == 2 ? (ushort)1 : (ushort)0, trailing: shape == 3);

        AssertMetadataFailure(fixture.Image());
    }

    [Theory]
    [InlineData("JSImport")]
    [InlineData("JSExport")]
    [InlineData("WitImport")]
    [InlineData("WitExport")]
    [InlineData("WitPostReturn")]
    public void ParseRejectsDuplicateHostDeclarations(string kind)
    {
        var fixture = new ImageFixture();
        var names = kind.StartsWith("Wit", StringComparison.Ordinal) ? new string?[] { "scope", "call" } : ["call"];
        var ns = kind.StartsWith("Wit", StringComparison.Ordinal) ? "System.Runtime.InteropServices.WebAssembly" : "System.Runtime.InteropServices.JavaScript";
        fixture.Attribute(fixture.Method, ns, kind + "Attribute", names);
        fixture.Attribute(fixture.Method, ns, kind + "Attribute", names);

        AssertMetadataFailure(fixture.Image(), "multiple");
    }

    [Fact]
    public void ParseRejectsConflictingHostBoundaries()
    {
        var fixture = new ImageFixture();
        fixture.Attribute(fixture.Method, "System.Runtime.InteropServices.JavaScript", "JSImportAttribute", ["call"]);
        fixture.Attribute(fixture.Method, "System.Runtime.InteropServices.JavaScript", "JSExportAttribute", ["call"]);

        AssertMetadataFailure(fixture.Image(), "more than one");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ParseValidatesInlineArrayLengthAndItsCompletePayload(int shape)
    {
        var fixture = new ImageFixture();
        fixture.Attribute(fixture.Owner, "", "UnrelatedAttribute", []);
        var length = shape == 1 ? 0 : 3;
        fixture.InlineArray(length, shape == 2 ? (ushort)0 : (ushort)1, shape == 3 ? (ushort)1 : (ushort)0, shape == 4);
        if (shape == 5)
            fixture.InlineArray(4);
        if (shape == 0)
        {
            using var parsed = Parse(fixture.Image());
            Assert.Equal(3, Assert.Single(parsed.Types.Values, type => type.Name == "Owner").InlineArrayLength);
        }
        else
            AssertMetadataFailure(fixture.Image(), "InlineArray");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParseRecognizesLocalEnumBasesAndLocalAttributeConstructors(bool flags)
    {
        var fixture = new ImageFixture();
        fixture.Metadata.AddFieldDefinition(FieldAttributes.Public, fixture.String("value__"), fixture.FieldSignature(PrimitiveTypeCode.Int32));
        var localEnum = fixture.Metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Abstract,
            fixture.String("System"), fixture.String("Enum"), fixture.TypeReference("System", "ValueType"),
            MetadataTokens.FieldDefinitionHandle(2), MetadataTokens.MethodDefinitionHandle(2));
        var choice = fixture.Metadata.AddTypeDefinition(TypeAttributes.Public | TypeAttributes.Sealed,
            fixture.String("Fixture"), fixture.String("Choice"), localEnum,
            MetadataTokens.FieldDefinitionHandle(2), MetadataTokens.MethodDefinitionHandle(2));
        fixture.Metadata.AddFieldDefinition(FieldAttributes.Public | FieldAttributes.SpecialName | FieldAttributes.RTSpecialName,
            fixture.String("value__"), fixture.FieldSignature(PrimitiveTypeCode.Int32));
        var localAttribute = fixture.Metadata.AddTypeDefinition(TypeAttributes.Public,
            flags ? fixture.String("System") : default, fixture.String(flags ? "FlagsAttribute" : "MarkerAttribute"),
            fixture.TypeReference("System", "Attribute"), MetadataTokens.FieldDefinitionHandle(3), MetadataTokens.MethodDefinitionHandle(2));
        var constructor = fixture.Metadata.AddMethodDefinition(MethodAttributes.Public, MethodImplAttributes.Runtime,
            fixture.String(".ctor"), fixture.MethodSignature(true, 0), -1, MetadataTokens.ParameterHandle(1));
        fixture.Metadata.AddCustomAttribute(choice, constructor, fixture.Payload([], 1, 0, false));
        var constructorReference = fixture.Metadata.AddMemberReference(
            localAttribute,
            fixture.String(".ctor"),
            fixture.MethodSignature(true, 0));
        fixture.Metadata.AddCustomAttribute(
            fixture.Owner,
            constructorReference,
            fixture.Payload([], 1, 0, false));

        using var parsed = Parse(fixture.Image());

        var parsedChoice = Assert.Single(parsed.Types.Values, type => type.Name == "Choice");
        Assert.True(parsedChoice.IsEnum);
        Assert.True(parsedChoice.IsValueType);
        Assert.Equal(flags, parsedChoice.IsFlagsEnum);
        Assert.Empty(parsedChoice.EnumMembers);
        Assert.Single(parsed.Fields.Values, field => field.Name == "value__" && field.DeclaringType == parsedChoice.Key);
        Assert.Single(parsed.Fields.Values, field => field.Name == "value__" && field.DeclaringType != parsedChoice.Key);
        Assert.Contains(parsed.Types.Values, type => type.Name == (flags ? "FlagsAttribute" : "MarkerAttribute"));
        Assert.Equal(MetadataTokens.GetToken(localAttribute), parsed.Methods[MetadataTokens.GetToken(constructor)].DeclaringType.MetadataToken);
        Assert.False(parsed.Methods[MetadataTokens.GetToken(constructor)].HasBody);
    }

    [Fact]
    public void ParseRejectsInvalidInputsBeforeReadingMetadata()
    {
        var native = new NativeFacts();
        var stackKinds = new StackKinds();
        Assert.Throws<ArgumentNullException>(() => ManagedAssembly.Parse("memory/app", [], AssemblyIdentityAliases.Empty, null!, native));
        Assert.Throws<ArgumentNullException>(() => ManagedAssembly.Parse("memory/app", null!, AssemblyIdentityAliases.Empty, stackKinds, native));
        Assert.Throws<ArgumentException>(() => ManagedAssembly.Parse(" ", [], AssemblyIdentityAliases.Empty, stackKinds, native));
        AssertMetadataFailure([]);
        Assert.Equal(0, native.Calls);
        Assert.Equal(0, stackKinds.Calls);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void ParseReadsOnlyWellFormedNullableAnnotations(int shape, bool nonNullable)
    {
        // Shapes: a wrong prolog, no value, one byte, a one-byte array and an empty
        // array. Only a readable not-null byte makes the export result non-nullable.
        var fixture = new ImageFixture();
        fixture.Attribute(fixture.Method, "System.Runtime.InteropServices.JavaScript", "JSExportAttribute", []);
        var payload = new BlobBuilder();
        payload.WriteUInt16(shape == 0 ? (ushort)0 : (ushort)1);
        if (shape is 0 or 2)
            payload.WriteByte(1);
        if (shape is 3 or 4)
            payload.WriteInt32(shape == 3 ? 1 : 0);
        if (shape == 3)
            payload.WriteByte(1);
        payload.WriteUInt16(0);
        fixture.RawAttribute(fixture.Method, "System.Runtime.CompilerServices", "NullableContextAttribute", payload);

        using var parsed = Parse(fixture.Image());

        Assert.Equal(nonNullable, Assert.Single(parsed.Methods.Values).JSExport!.NonNullableResult);
    }

    private static MethodBuilder Method(TypeBuilder owner, string name, MethodAttributes attributes, Type returns, Type[] parameters)
    {
        var method = owner.DefineMethod(name, attributes, returns, parameters);
        var cil = method.GetILGenerator();
        if (returns != typeof(void))
        {
            var value = cil.DeclareLocal(returns);
            cil.Emit(OpCodes.Ldloca, value);
            cil.Emit(OpCodes.Initobj, returns);
            cil.Emit(OpCodes.Ldloc, value);
        }
        cil.Emit(OpCodes.Ret);
        return method;
    }

    private static ManagedAssembly Parse(byte[] image) => ManagedAssembly.Parse("memory/contract.dll", image,
        AssemblyIdentityAliases.Empty, new StackKinds(), new NativeFacts());

    private static void AssertMetadataFailure(byte[] image, string? message = null)
    {
        var exception = Assert.Throws<CompilerException>(() => Parse(image));
        Assert.Equal(DiagnosticCode.UnsupportedMetadata, exception.Diagnostic.Code);
        if (message is not null)
            Assert.Contains(message, exception.Message);
    }

    private sealed class StackKinds : IValueTypeDefinitionStackKindResolver
    {
        public int Calls { get; private set; }
        public CliValueKind Resolve(string canonicalName)
        {
            Calls++;
            return CliValueKind.ValueType;
        }
    }

    private sealed class NativeFacts : INativeImportDeclarationReader
    {
        public int Calls { get; private set; }
        public NativeImportDeclaration Declaration { get; } = new("library", "entry", MethodImportAttributes.CallingConventionCDecl, false, false, false, false);
        public NativeImportDeclaration? Read(MetadataReader metadata, MethodDefinition method)
        {
            Calls++;
            return Declaration;
        }
    }

    private sealed class ImageFixture
    {
        public ImageFixture()
        {
            Metadata.AddModule(0, String("Contract.dll"), Metadata.GetOrAddGuid(Guid.Empty), default, default);
            Metadata.AddAssembly(String("Contract"), new(1, 0), default, default, 0, 0);
            Metadata.AddTypeDefinition(TypeAttributes.NotPublic, default, String("<Module>"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            Owner = Metadata.AddTypeDefinition(TypeAttributes.Public, String("Fixture"), String("Owner"), default,
                MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
            Method = Metadata.AddMethodDefinition(MethodAttributes.Public | MethodAttributes.Static, MethodImplAttributes.Runtime,
                String("Run"), MethodSignature(false, 0), -1, MetadataTokens.ParameterHandle(1));
        }

        public MetadataBuilder Metadata { get; } = new();
        public TypeDefinitionHandle Owner { get; }
        public MethodDefinitionHandle Method { get; }
        public StringHandle String(string value) => Metadata.GetOrAddString(value);
        public TypeReferenceHandle TypeReference(string ns, string name) => Metadata.AddTypeReference(
            Metadata.AddAssemblyReference(String("System.Runtime"), new(10, 0), default, default, 0, default), String(ns), String(name));
        public BlobHandle FieldSignature(PrimitiveTypeCode type)
        {
            var blob = new BlobBuilder();
            new BlobEncoder(blob).FieldSignature().PrimitiveType(type);
            return Metadata.GetOrAddBlob(blob);
        }
        public BlobHandle MethodSignature(bool instance, int strings)
        {
            var blob = new BlobBuilder();
            new BlobEncoder(blob).MethodSignature(isInstanceMethod: instance).Parameters(strings,
                returns => returns.Void(), parameters =>
                {
                    for (var i = 0; i < strings; i++)
                        parameters.AddParameter().Type().String();
                });
            return Metadata.GetOrAddBlob(blob);
        }
        public BlobHandle Payload(string?[] strings, ushort prolog, ushort named, bool trailing)
        {
            var blob = new BlobBuilder();
            blob.WriteUInt16(prolog);
            foreach (var value in strings)
                blob.WriteSerializedString(value);
            blob.WriteUInt16(named);
            if (trailing)
                blob.WriteByte(1);
            return Metadata.GetOrAddBlob(blob);
        }
        public void Attribute(EntityHandle owner, string ns, string name, string?[] strings,
            ushort prolog = 1, ushort named = 0, bool trailing = false) => Metadata.AddCustomAttribute(owner,
            Metadata.AddMemberReference(TypeReference(ns, name), String(".ctor"), MethodSignature(true, strings.Length)),
            Payload(strings, prolog, named, trailing));
        public void RawAttribute(EntityHandle owner, string ns, string name, BlobBuilder payload) => Metadata.AddCustomAttribute(owner,
            Metadata.AddMemberReference(TypeReference(ns, name), String(".ctor"), MethodSignature(true, 0)),
            Metadata.GetOrAddBlob(payload));
        public void InlineArray(int length, ushort prolog = 1, ushort named = 0, bool trailing = false)
        {
            var signature = new BlobBuilder();
            new BlobEncoder(signature).MethodSignature(isInstanceMethod: true).Parameters(1,
                returns => returns.Void(), parameters => parameters.AddParameter().Type().Int32());
            var payload = new BlobBuilder();
            payload.WriteUInt16(prolog);
            payload.WriteInt32(length);
            payload.WriteUInt16(named);
            if (trailing)
                payload.WriteByte(1);
            Metadata.AddCustomAttribute(Owner, Metadata.AddMemberReference(TypeReference("System.Runtime.CompilerServices", "InlineArrayAttribute"),
                String(".ctor"), Metadata.GetOrAddBlob(signature)), Metadata.GetOrAddBlob(payload));
        }
        public byte[] Image(BlobBuilder? data = null)
        {
            var image = new BlobBuilder();
            new ManagedPEBuilder(new(imageCharacteristics: Characteristics.Dll), new(Metadata), new BlobBuilder(),
                mappedFieldData: data).Serialize(image);
            return image.ToArray();
        }
    }
}
