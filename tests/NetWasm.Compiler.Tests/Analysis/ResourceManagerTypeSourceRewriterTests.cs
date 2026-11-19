// Copyright (c) 2026 Zion Sati and NetWasm contributors (https://github.com/zion-sati/NetWasm).
// SPDX-License-Identifier: LicenseRef-NetWasm-Community-1.0

using System.Collections.Immutable;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Tests.Analysis;

// new ResourceManager(typeof(T)) becomes T's handle, T's base name (its namespace, a dot, its name; its name alone in the
// global namespace) and ResourceManager's (RuntimeTypeHandle, string) constructor; without that constructor the code
// stays as written.
public sealed class ResourceManagerTypeSourceRewriterTests
{
    private static readonly AssemblyIdentity CoreLib = new("NetWasm.CoreLib");
    private static readonly AssemblyIdentity App = new("App");
    private static readonly EntityKey ResourceManagerKey = new(CoreLib, 0x02000001);
    private static readonly EntityKey TypeKey = new(CoreLib, 0x02000002);
    private static readonly CliTypeIdentity ResourceManager = CliTypeIdentity.Named(CoreLib, "System.Resources", "ResourceManager", false);
    private static readonly CliTypeIdentity SystemType = CliTypeIdentity.Named(CoreLib, "System", "Type", false);
    private static readonly CliTypeIdentity Handle = CliTypeIdentity.Named(CoreLib, "System", "RuntimeTypeHandle", true);
    private static readonly CliTypeIdentity String = CliTypeIdentity.Primitive("string", CliValueKind.ManagedReference, false);
    private static readonly MethodDefinitionModel FromType = Method(0x06000001, ResourceManagerKey, ".ctor", false, SystemType);
    private static readonly MethodDefinitionModel FromHandle = Method(0x06000002, ResourceManagerKey, ".ctor", false, Handle, String);
    private static readonly MethodDefinitionModel GetTypeFromHandle = Method(0x06000003, TypeKey, "GetTypeFromHandle", true, Handle);

    [Theory]
    [InlineData("App.Resources", "App.Resources.Strings")]
    [InlineData("", "Strings")]
    public void ATypesResourcesAreNamedByItsNamespaceAndName(string @namespace, string baseName)
    {
        var source = CliTypeIdentity.Named(App, @namespace, "Strings", false);

        var rewritten = Rewriter(withHandleConstructor: true, @namespace).Rewrite(Body(source));

        Assert.Equal([CilOperation.LoadTypeToken, CilOperation.LoadString, CilOperation.NewObject, CilOperation.Return],
            rewritten.Instructions.Select(instruction => instruction.Operation));
        Assert.Equal(new CilOperand.UserString(baseName), rewritten.Instructions[1].Operand);
        Assert.Equal(FromHandle, ((CilOperand.MethodInstance)rewritten.Instructions[2].Operand).Value.Definition);
        Assert.Equal(3, rewritten.MaxStack);
    }

    [Fact]
    public void WithoutTheHandleConstructorTheCodeStays()
    {
        var body = Body(CliTypeIdentity.Named(App, "App", "Strings", false));

        Assert.Same(body, Rewriter(withHandleConstructor: false, "App").Rewrite(body));
    }

    private static ResourceManagerTypeSourceRewriter Rewriter(bool withHandleConstructor, string @namespace)
    {
        var fakes = new Fakes(withHandleConstructor, @namespace);
        return new(fakes, fakes, fakes, fakes, fakes);
    }

    // ldtoken T; call Type.GetTypeFromHandle; newobj ResourceManager(Type); ret
    private static CilMethodBody Body(CliTypeIdentity source)
    {
        var run = Method(0x06000010, new EntityKey(App, 0x02000010), "Run", true, ResourceManager);
        return new CilMethodBody(run, 2, [],
        [
            new(0, 1, CilOperation.LoadTypeToken, new CilOperand.TypeIdentity(source)),
            new(1, 2, CilOperation.Call, new CilOperand.MethodInstance(Instance(GetTypeFromHandle, SystemType))),
            new(2, 3, CilOperation.NewObject, new CilOperand.MethodInstance(Instance(FromType, ResourceManager))),
            new(3, 4, CilOperation.Return, new CilOperand.None()),
        ]);
    }

    private static MethodDefinitionModel Method(int token, EntityKey owner, string name, bool isStatic, params CliTypeIdentity[] parameters)
    {
        var assembly = owner.Assembly;
        return new MethodDefinitionModel(new EntityKey(assembly, token), owner, name, isStatic,
            new MethodSignatureModel(CliTypeIdentity.FromStackKind(CliValueKind.Void), [.. parameters]), 1);
    }

    private static MethodInstanceModel Instance(MethodDefinitionModel definition, CliTypeIdentity owner) =>
        new(definition, owner, [], definition.Signature);

    // ResourceManager's constructors, T's definition in the given namespace, and symbols by key.
    private sealed class Fakes(bool withHandleConstructor, string @namespace) :
        ICalledMethodResolver, ITypeDefinitionResolver, IMethodRepository, IMethodInstanceResolver, ISymbolFormatter
    {
        public MethodInstanceModel? Resolve(CilInstruction instruction) => (instruction.Operand as CilOperand.MethodInstance)?.Value;

        public TypeDefinitionModel ResolveTypeIdentity(CliTypeIdentity identity) => identity.Equals(ResourceManager)
            ? new(ResourceManagerKey, "System.Resources", "ResourceManager", false, [],
                withHandleConstructor ? [FromType.Key, FromHandle.Key] : [FromType.Key])
            : new(new EntityKey(App, 0x02000020), @namespace, "Strings", false, [], []);

        public MethodDefinitionModel GetMethod(EntityKey key) => key == FromHandle.Key ? FromHandle : FromType;

        public MethodInstanceModel ResolveMethodInstance(AssemblyIdentity source, int metadataToken, string methodDisplayName,
            int ilOffset, CliGenericContext? genericContext = null) =>
            Instance(GetMethod(new EntityKey(source, metadataToken)), ResourceManager);

        public string Format(EntityKey key) => key == ResourceManagerKey ? "System.Resources.ResourceManager"
            : key == TypeKey ? "System.Type"
            : key.ToString();

        public string Format(MethodDefinitionModel method) => method.Name;
    }
}
