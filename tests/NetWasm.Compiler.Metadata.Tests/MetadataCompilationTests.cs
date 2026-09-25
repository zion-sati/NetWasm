using System.Reflection.Metadata.Ecma335;
using NetWasm.Compiler.Core;
using NetWasm.TestInfrastructure;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class MetadataCompilationTests
{
    [Fact]
    public void CompilationResolvesCrossAssemblyMembersAndDecodesSupportedCil()
    {
        using var assets = TestAssets.Create();
        using var lease = MetadataCompilationTestFactory.Load(
            assets.Application,
            [assets.Library, assets.CoreLib]);

        var snapshot = lease.Snapshot;
        var symbols = MetadataCapabilityTestData.Symbols(snapshot);
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var methods = MetadataCapabilityTestData.MethodFinder(snapshot);
        var run = methods.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "NetWasm.Fixtures.Application.EntryPoint",
            "Run");
        var body = bodies.ReadMethodBody(run);
        var operations = body.Instructions
            .Select(instruction => instruction.Operation)
            .Distinct()
            .ToArray();

        Assert.Equal(3, snapshot.Assemblies.Length);
        Assert.Equal("NetWasm.Fixtures.Application.EntryPoint::Run", symbols.Format(run));
        Assert.Equal(
            "NetWasm.Fixtures.Application.EntryPoint",
            symbols.Format(run.DeclaringType));
        Assert.Contains(CilOperation.LoadArgument, operations);
        Assert.Contains(CilOperation.LoadLocal, operations);
        Assert.Contains(CilOperation.StoreLocal, operations);
        Assert.Contains(CilOperation.LoadInt32, operations);
        Assert.Contains(CilOperation.LoadString, operations);
        Assert.Contains(CilOperation.LoadStaticField, operations);
        Assert.Contains(CilOperation.Add, operations);
        Assert.Contains(CilOperation.CompareGreaterThanSigned, operations);
        Assert.Contains(CilOperation.CompareLessThanSigned, operations);
        Assert.Contains(CilOperation.Branch, operations);
        Assert.Contains(CilOperation.BranchIfFalse, operations);
        Assert.Contains(CilOperation.Call, operations);
        Assert.Contains(CilOperation.CallVirtual, operations);
        Assert.Contains(CilOperation.NewObject, operations);
        Assert.Contains(CilOperation.Return, operations);
        Assert.Contains(
            body.Instructions,
            instruction => instruction.Operand is CilOperand.UserString text &&
                           text.Value.Contains('\ud800'));

    }

    [Fact]
    public void CompilationPreservesStructuralSignaturesAndResolvesReferenceOverloads()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource(
            "Signature.Library",
            """
            namespace Signature.Library;

            public sealed class Box<T>
            {
                public T Value;
            }

            public struct Pair
            {
                public int Number;
                public string Text;
            }

            public static class Outer
            {
                public sealed class Inner { }
            }

            public static class Overloads
            {
                public static int Pick(string value) => 1;
                public static int Pick(object value) => 2;
                public static int Pick(bool value) => 3;
                public static int Pick(char value) => 4;
                public static int Pick(int value) => 5;
            }
            """);
        var application = assets.CompileSource(
            "Signature.Application",
            """
            using Signature.Library;

            namespace Signature.Application;

            public static class EntryPoint
            {
                public static int Run(int input) =>
                    Overloads.Pick((string)null) +
                    Overloads.Pick((object)null) +
                    Overloads.Pick(false) +
                    Overloads.Pick('x') +
                    Overloads.Pick(input);

                public static int HasClosedGenericLocal(Box<string> value)
                {
                    Box<string> copy = value;
                    return copy == null ? 0 : 1;
                }
            }
            """,
            library);

        using var lease = MetadataCompilationTestFactory.Load(
            application,
            [library, assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var types = MetadataCapabilityTestData.Types(snapshot);
        var fields = MetadataCapabilityTestData.Fields(snapshot);
        var methods = MetadataCapabilityTestData.Methods(snapshot);
        var symbols = MetadataCapabilityTestData.Symbols(snapshot);
        var typeFinder = MetadataCapabilityTestData.TypeFinder(snapshot);
        var typeIdentities = MetadataCapabilityTestData.TypeIdentities(snapshot);
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var methodFinder = MetadataCapabilityTestData.MethodFinder(snapshot);
        var run = methodFinder.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "Signature.Application.EntryPoint",
            "Run");
        var runBody = bodies.ReadMethodBody(run);
        var calls = runBody.Instructions
            .Where(instruction => instruction.Operation == CilOperation.Call)
            .Select(instruction =>
                Assert.IsType<CilOperand.MethodInstance>(instruction.Operand).Value)
            .ToArray();

        Assert.Equal(5, calls.Length);
        Assert.Equal(5, calls.Select(call => call.CanonicalName).Distinct().Count());
        var overloadParameters = calls
            .Select(call => Assert.Single(call.Signature.ParameterSignatureTypes))
            .ToArray();
        Assert.Equal(5, overloadParameters.Distinct().Count());
        Assert.Single(overloadParameters, type => type.CanonicalName == "primitive:string");
        Assert.Single(overloadParameters, type => type.CanonicalName == "primitive:object");
        Assert.Single(overloadParameters, type => type.CanonicalName == "primitive:bool");
        Assert.Single(overloadParameters, type => type.CanonicalName == "primitive:char");
        Assert.Single(overloadParameters, type => type.CanonicalName == "primitive:i4");

        var genericLocal = methodFinder.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "Signature.Application.EntryPoint",
            "HasClosedGenericLocal");
        var genericBody = bodies.ReadMethodBody(genericLocal);
        var localType = Assert.Single(
            genericBody.LocalSignatureTypes,
            type => type.Shape == CliTypeShape.GenericInstantiation);
        Assert.Equal(CliTypeShape.GenericInstantiation, localType.Shape);
        Assert.Equal(
            "[Signature.Library]Signature.Library.Box`1<primitive:string>",
            localType.CanonicalName);

        var pair = typeFinder.FindType("Signature.Library.Pair");
        Assert.True(pair.IsValueType);
        Assert.Equal(
            ["primitive:i4", "primitive:string"],
            pair.Fields.Select(fields.GetField)
                .Select(field => field.SignatureType.CanonicalName));
        var inner = typeFinder.FindType("Signature.Library.Outer+Inner");
        Assert.Equal(
            "[Signature.Library]Signature.Library.Outer+Inner",
            typeIdentities.GetTypeIdentity(inner.Key).CanonicalName);

    }

    [Fact]
    public void CompilationResolvesClosedTypeAndMethodInstancesStructurally()
    {
        using var assets = TestAssets.Create();
        var library = assets.CompileSource(
            "Generic.Library",
            """
            namespace Generic.Library;

            public sealed class Box<T>
            {
                public T Echo(T value) => value;
            }

            public static class GenericMethods
            {
                public static T Identity<T>(T value)
                {
                    T copy = value;
                    return copy;
                }
            }
            """);
        var application = assets.CompileSource(
            "Generic.Application",
            """
            using Generic.Library;

            namespace Generic.Application;

            public static class EntryPoint
            {
                public static int Run(int input)
                {
                    var box = new Box<int>();
                    return box.Echo(input) + GenericMethods.Identity(input);
                }
            }
            """,
            library);
        using var lease = MetadataCompilationTestFactory.Load(
            application,
            [library, assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var instances = MetadataCapabilityTestData.MethodInstances(snapshot);
        var bodies = MetadataCapabilityTestData.MethodBodies(snapshot);
        var methods = MetadataCapabilityTestData.MethodFinder(snapshot);
        var source = snapshot.Assemblies.Single(
            assembly => assembly.Identity.Name == snapshot.EntryAssemblyIdentity.Name);
        var echoHandle = source.Reader.MemberReferences.Single(handle =>
            source.Reader.GetString(source.Reader.GetMemberReference(handle).Name) == "Echo");
        var echo = instances.ResolveMethodInstance(
            source.Identity,
            MetadataTokens.GetToken(echoHandle),
            "Generic.Application.EntryPoint::Run",
            0);
        Assert.True(echo.IsConstructed);
        Assert.Equal(CliTypeShape.GenericInstantiation, echo.DeclaringType.Shape);
        Assert.Equal("primitive:i4", Assert.Single(echo.DeclaringType.TypeArguments).CanonicalName);
        Assert.Equal("primitive:i4", Assert.Single(echo.Signature.ParameterSignatureTypes).CanonicalName);
        Assert.Equal("primitive:i4", echo.Signature.ReturnSignatureType.CanonicalName);

        var methodSpecificationRows = source.Reader.GetTableRowCount(TableIndex.MethodSpec);
        Assert.Equal(1, methodSpecificationRows);
        var identity = instances.ResolveMethodInstance(
            source.Identity,
            MetadataTokens.GetToken(MetadataTokens.MethodSpecificationHandle(1)),
            "Generic.Application.EntryPoint::Run",
            0);
        Assert.True(identity.IsConstructed);
        Assert.Equal("primitive:i4", Assert.Single(identity.MethodArguments).CanonicalName);
        Assert.Equal("primitive:i4", Assert.Single(identity.Signature.ParameterSignatureTypes).CanonicalName);
        Assert.NotEqual(echo.CanonicalName, identity.CanonicalName);
        var identityBody = bodies.ReadMethodBody(identity);
        Assert.Same(identity, identityBody.MethodInstance);
        Assert.Contains(
            identityBody.LocalSignatureTypes,
            type => type.CanonicalName == "primitive:i4");

        var run = methods.FindMethod(
            snapshot.EntryAssemblyIdentity,
            "Generic.Application.EntryPoint",
            "Run");
        var body = bodies.ReadMethodBody(run);
        var constructedCalls = body.Instructions
            .Select(instruction => instruction.Operand)
            .OfType<CilOperand.MethodInstance>()
            .Select(operand => operand.Value)
            .ToArray();
        Assert.Equal(3, constructedCalls.Length);
        Assert.Contains(constructedCalls, method => method.Definition.Name == ".ctor");
        Assert.Contains(constructedCalls, method => method.Definition.Name == "Echo");
        Assert.Contains(constructedCalls, method => method.Definition.Name == "Identity");

    }

    [Fact]
    public void CompilationRejectsMissingAndDuplicateAssemblyClosure()
    {
        using var assets = TestAssets.Create();
        var missing = Assert.Throws<CompilerException>(
            () => MetadataCompilationTestFactory.Load(
                assets.Application,
                [assets.CoreLib]));
        Assert.Equal(DiagnosticCode.AssemblyResolution, missing.Diagnostic.Code);
        Assert.Contains("unresolved reference 'Fixture.Library'", missing.Message);

        var duplicate = Assert.Throws<CompilerException>(
            () => MetadataCompilationTestFactory.Load(
                assets.Application,
                [assets.Library, assets.CoreLib, assets.CoreLib]));
        Assert.Equal(DiagnosticCode.DuplicateAssembly, duplicate.Diagnostic.Code);
        Assert.Contains("duplicate assembly identity 'NetWasm.CoreLib'", duplicate.Message);
    }

    [Fact]
    public void LookupFailuresAreTypedAndDisposalIsIdempotent()
    {
        using var assets = TestAssets.Create();
        var lease = MetadataCompilationTestFactory.Load(
            assets.Application,
            [assets.Library, assets.CoreLib]);
        var snapshot = lease.Snapshot;
        var methods = MetadataCapabilityTestData.MethodFinder(snapshot);
        var missingType = Assert.Throws<CompilerException>(
            () => methods.FindMethod(
                snapshot.EntryAssemblyIdentity,
                "Missing.Type",
                "Run"));
        Assert.Equal(DiagnosticCode.InvalidEntryPoint, missingType.Diagnostic.Code);

        var missingMethod = Assert.Throws<CompilerException>(
            () => methods.FindMethod(
                snapshot.EntryAssemblyIdentity,
                "NetWasm.Fixtures.Application.EntryPoint",
                "Missing"));
        Assert.Contains("was not found uniquely", missingMethod.Message);


        lease.Dispose();
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = lease.Snapshot);
    }

    [Fact]
    public void CompilationValidatesArguments()
    {
        Assert.Throws<ArgumentException>(
            () => MetadataCompilationTestFactory.Load("", []));
        Assert.Throws<ArgumentNullException>(
            () => MetadataCompilationTestFactory.Load("entry.dll", null!));
    }

    [Fact]
    public void CoreLibExposesTheSupportedStringProfile()
    {
        using var assets = TestAssets.Create();
        using var coreLib = ManagedAssemblyTestFactory.Load(assets.CoreLib);
        var requiredContracts = new[]
        {
            "System.Object",
            "System.ValueType",
            "System.Enum",
            "System.Int32",
            "System.Char",
            "System.String",
            "System.Attribute",
            "System.AttributeUsageAttribute",
            "System.Reflection.DefaultMemberAttribute",
            "System.Runtime.CompilerServices.IndexerNameAttribute",
        };
        var names = coreLib.Types.Values
            .Select(type => type.FullName)
            .ToArray();
        Assert.All(requiredContracts, name => Assert.Contains(name, names));

        var stringType = coreLib.Types.Values.Single(
            type => type.FullName == "System.String");
        var members = stringType.Methods
            .Select(key => coreLib.Methods[key.MetadataToken].Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var requiredMembers = new[]
        {
            ".cctor", ".ctor", "AddLength", "BuildReplacement", "CompareOrdinal",
            "Concat", "Concat", "Concat", "Concat", "Contains", "Copy",
            "CountMatches", "Create", "EndsWith", "Equals", "Equals", "Equals",
            "Format", "Format", "Format", "Format", "GetHashCode",
            "IndexOf", "IndexOf", "IndexOf", "IndexOf",
            "IndexOf", "IndexOf", "Insert", "IsNullOrEmpty", "IsWhiteSpace",
            "Join", "LastIndexOf", "MatchesAt", "PadLeft", "PadLeft", "PadRight",
            "PadRight", "Remove", "Remove", "Replace", "Replace",
            "SetCharUnchecked", "Split", "StartsWith", "Substring", "Substring",
            "ToCharArray", "Trim", "Trim", "TrimEnd", "TrimStart", "TrimWhiteSpace",
            "ValidateOrdinal", "ValidateRange", "get_Chars", "get_Length",
            "op_Equality", "op_Inequality",
        };
        Assert.All(requiredMembers, member => Assert.Contains(member, members));
        Assert.Equal(15, members.Count(member => member == "Format"));
        Assert.Equal(15, members.Count(member => member == "Concat"));
        Assert.Contains("ToLower", members);
        Assert.Contains("ToUpper", members);
        Assert.Contains("Normalize", members);
    }
}
