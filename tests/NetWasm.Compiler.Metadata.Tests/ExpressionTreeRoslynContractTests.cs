using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Metadata.Tests;

public sealed class ExpressionTreeRoslynContractTests
{
    private static readonly ImmutableDictionary<ushort, OpCode> OpCodesByValue =
        typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToImmutableDictionary(opCode => unchecked((ushort)opCode.Value));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PinnedRoslynEmitsTheBoundedExpressionMetadataContract(bool optimized)
    {
        var image = Compile(optimized);
        using var stream = new MemoryStream(image, writable: false);
        using var peReader = new PEReader(stream);
        var reader = peReader.GetMetadataReader();

        AssertFactoryShape(peReader, reader, "Arithmetic", "Add");
        AssertFactoryShape(peReader, reader, "Conditional", "Condition", "AndAlso");
        AssertMemberShape(peReader, reader, "Property", "Property", "MethodBase", "GetMethodFromHandle", HandleKind.MethodDefinition);
        AssertMemberShape(peReader, reader, "Field", "Field", "FieldInfo", "GetFieldFromHandle", HandleKind.FieldDefinition);
        AssertMemberShape(peReader, reader, "Constructor", "New", "MethodBase", "GetMethodFromHandle", HandleKind.MethodDefinition);
        AssertMemberShape(peReader, reader, "InstanceMethod", "Call", "MethodBase", "GetMethodFromHandle", HandleKind.MethodDefinition);
        AssertMemberShape(peReader, reader, "StaticMethod", "Call", "MethodBase", "GetMethodFromHandle", HandleKind.MethodDefinition);
        AssertMemberShape(peReader, reader, "Operator", "Add", "MethodBase", "GetMethodFromHandle", HandleKind.MethodDefinition);
        AssertMemberShape(peReader, reader, "Conversion", "Convert", "MethodBase", "GetMethodFromHandle", HandleKind.MethodDefinition);
        AssertMemberShape(peReader, reader, "Closure", "Field", "FieldInfo", "GetFieldFromHandle", HandleKind.FieldDefinition);
        AssertMemberShape(peReader, reader, "Quote", "Quote", "MethodBase", "GetMethodFromHandle", HandleKind.MethodDefinition);
        AssertMemberShape(peReader, reader, "GenericCall", "Call", "MethodBase", "GetMethodFromHandle", HandleKind.MemberReference);
        AssertMemberShape(peReader, reader, "GenericMethodCall", "Call", "MethodBase", "GetMethodFromHandle", HandleKind.MethodSpecification);
        AssertExactFactorySignatures(peReader, reader);
        AssertExactHandleFactorySignatures(peReader, reader);

        foreach (var methodName in FactoryMethodNames)
        {
            var observations = Observe(peReader, reader, methodName);
            AssertCall(observations, "Expression", "Parameter", $"common:{methodName}:parameter");
            AssertCall(observations, "Expression", "Lambda", $"common:{methodName}:lambda");
            AssertCall(observations, "Type", "GetTypeFromHandle", $"common:{methodName}:type-handle");
            Assert.True(observations.Any(observation =>
                observation.OpCode == "ldtoken" &&
                observation.TargetKind is HandleKind.TypeDefinition or
                    HandleKind.TypeReference or HandleKind.TypeSpecification),
                $"common:{methodName}:type-token");
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DesktopExecutesRepresentativeTreesThroughBothCompilePaths(
        bool optimized,
        bool preferInterpretation)
    {
        var image = Compile(optimized);
        var loadContext = new AssemblyLoadContext(
            $"expression-tree-contract-{optimized}-{preferInterpretation}",
            isCollectible: true);
        try
        {
            using var stream = new MemoryStream(image, writable: false);
            var assembly = loadContext.LoadFromStream(stream);
            var shapes = assembly.GetType("ExpressionTreeRoslynShapes.Shapes")!;
            var run = shapes.GetMethod("RunAll", BindingFlags.Public | BindingFlags.Static)!;

            Assert.Equal(42, run.Invoke(null, [preferInterpretation]));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static void AssertFactoryShape(
        PEReader peReader,
        MetadataReader reader,
        string methodName,
        params string[] factoryNames)
    {
        var observations = Observe(peReader, reader, methodName);
        foreach (var factoryName in factoryNames)
        {
            AssertCall(
                observations,
                "Expression",
                factoryName,
                $"factory:{methodName}:{factoryName}");
        }
    }

    private static void AssertExactFactorySignatures(
        PEReader peReader,
        MetadataReader reader)
    {
        const string expression =
            "[System.Linq.Expressions]System.Linq.Expressions.Expression";
        const string parameterExpression =
            "[System.Linq.Expressions]System.Linq.Expressions.ParameterExpression";
        const string methodInfo =
            "[System.Private.CoreLib]System.Reflection.MethodInfo";
        const string fieldInfo =
            "[System.Private.CoreLib]System.Reflection.FieldInfo";
        const string constructorInfo =
            "[System.Private.CoreLib]System.Reflection.ConstructorInfo";
        const string type = "[System.Private.CoreLib]System.Type";
        const string model =
            "[ExpressionTreeRoslynShapes]ExpressionTreeRoslynShapes.Model";
        var binary = $"static<0> {Node("BinaryExpression")}({expression},{expression})";
        var unary = $"static<0> {Node("UnaryExpression")}({expression})";
        var call = $"static<0> {Node("MethodCallExpression")}({expression},{methodInfo},{expression}[])";

        AssertSignature("Arithmetic", "Add", binary);
        AssertSignature("Conditional", "GreaterThan", binary);
        AssertSignature("Conditional", "LessThan", binary);
        AssertSignature("Conditional", "AndAlso", binary);
        AssertSignature("Conditional", "Negate", unary);
        AssertSignature(
            "Conditional",
            "Condition",
            $"static<0> {Node("ConditionalExpression")}({expression},{expression},{expression})");
        AssertSignature(
            "Property",
            "Property",
            $"static<0> {Node("MemberExpression")}({expression},{methodInfo})");
        AssertSignature(
            "Field",
            "Field",
            $"static<0> {Node("MemberExpression")}({expression},{fieldInfo})");
        AssertSignature(
            "Constructor",
            "New",
            $"static<0> {Node("NewExpression")}({constructorInfo},[System.Private.CoreLib]System.Collections.Generic.IEnumerable`1<{expression}>)");
        AssertSignature("InstanceMethod", "Call", call);
        AssertSignature("StaticMethod", "Call", call);
        AssertSignature(
            "Operator",
            "Add",
            $"static<0> {Node("BinaryExpression")}({expression},{expression},{methodInfo})");
        AssertSignature(
            "Conversion",
            "Convert",
            $"static<0> {Node("UnaryExpression")}({expression},{type},{methodInfo})");
        AssertSignature("Quote", "Quote", unary);
        AssertSignature("Quote", "Call", call);
        AssertSignature("GenericCall", "Call", call);
        AssertSignature("GenericMethodCall", "Call", call);

        foreach (var methodName in FactoryMethodNames)
        {
            AssertSignature(
                methodName,
                "Parameter",
                $"static<0> {parameterExpression}({type},primitive:string)");
            var (parameter, result) = methodName switch
            {
                "Property" or "Field" or "InstanceMethod" or "Conversion" =>
                    (model, "primitive:i4"),
                "Constructor" => ("primitive:i4", model),
                "Operator" => (model, model),
                _ => ("primitive:i4", "primitive:i4"),
            };
            var delegateType =
                $"[System.Private.CoreLib]System.Func`2<{parameter},{result}>";
            AssertSignature(
                methodName,
                "Lambda",
                $"static<1> {expression}`1<{delegateType}>({expression},{parameterExpression}[])");
        }

        foreach (var methodName in new[]
                 {
                     "Arithmetic",
                     "Conditional",
                     "InstanceMethod",
                     "Operator",
                     "Closure",
                     "Quote",
                 })
        {
            AssertSignature(
                methodName,
                "Constant",
                $"static<0> {Node("ConstantExpression")}(primitive:object,{type})");
        }

        static string Node(string name) =>
            "[System.Linq.Expressions]System.Linq.Expressions." + name;

        void AssertSignature(string methodName, string factory, string signature) =>
            AssertCallSignature(
                Observe(peReader, reader, methodName),
                "Expression",
                factory,
                signature,
                $"signature:{methodName}:{factory}");
    }

    private static void AssertExactHandleFactorySignatures(
        PEReader peReader,
        MetadataReader reader)
    {
        const string core = "[System.Private.CoreLib]System.";
        const string reflection = core + "Reflection.";
        var typeHandleFactory =
            $"static<0> {core}Type({core}RuntimeTypeHandle)";
        var methodHandleFactory =
            $"static<0> {reflection}MethodBase({core}RuntimeMethodHandle)";
        var contextualMethodHandleFactory =
            $"static<0> {reflection}MethodBase({core}RuntimeMethodHandle,{core}RuntimeTypeHandle)";
        var fieldHandleFactory =
            $"static<0> {reflection}FieldInfo({core}RuntimeFieldHandle)";

        foreach (var methodName in FactoryMethodNames)
        {
            AssertSignature(
                methodName,
                "Type",
                "GetTypeFromHandle",
                typeHandleFactory);
        }
        foreach (var methodName in new[]
                 {
                     "Property",
                     "Constructor",
                     "InstanceMethod",
                     "StaticMethod",
                     "Operator",
                     "Conversion",
                     "Quote",
                     "GenericMethodCall",
                 })
        {
            AssertSignature(
                methodName,
                "MethodBase",
                "GetMethodFromHandle",
                methodHandleFactory);
        }
        AssertSignature(
            "GenericCall",
            "MethodBase",
            "GetMethodFromHandle",
            contextualMethodHandleFactory);
        foreach (var methodName in new[] { "Field", "Closure" })
        {
            AssertSignature(
                methodName,
                "FieldInfo",
                "GetFieldFromHandle",
                fieldHandleFactory);
        }

        void AssertSignature(
            string methodName,
            string owner,
            string factory,
            string signature) => AssertCallSignature(
                Observe(peReader, reader, methodName),
                owner,
                factory,
                signature,
                $"handle-signature:{methodName}:{owner}:{factory}");
    }

    private static void AssertMemberShape(
        PEReader peReader,
        MetadataReader reader,
        string methodName,
        string factoryName,
        string handleOwner,
        string handleFactory,
        HandleKind tokenKind)
    {
        var observations = Observe(peReader, reader, methodName);
        AssertCall(observations, "Expression", factoryName, $"member:{methodName}:factory");
        AssertCall(observations, handleOwner, handleFactory, $"member:{methodName}:handle-factory");
        Assert.True(observations.Any(observation =>
            observation.OpCode == "ldtoken" && observation.TargetKind == tokenKind),
            $"member:{methodName}:token-kind:{tokenKind}");
    }

    private static void AssertCall(
        ImmutableArray<CilObservation> observations,
        string owner,
        string member,
        string contract) =>
        Assert.True(observations.Any(observation =>
            observation.OpCode is "call" or "callvirt" &&
            observation.Owner == owner &&
            observation.Member == member), contract);

    private static void AssertCallSignature(
        ImmutableArray<CilObservation> observations,
        string owner,
        string member,
        string signature,
        string contract)
    {
        var calls = observations.Where(observation =>
            observation.OpCode is "call" or "callvirt" &&
            observation.Owner == owner &&
            observation.Member == member).ToArray();
        Assert.True(calls.Length > 0, contract);
        Assert.All(calls, call => Assert.Equal(signature, call.Signature));
    }

    private static ImmutableArray<CilObservation> Observe(
        PEReader peReader,
        MetadataReader reader,
        string methodName)
    {
        var methodHandle = reader.TypeDefinitions
            .Select(reader.GetTypeDefinition)
            .Where(type => reader.GetString(type.Namespace) == "ExpressionTreeRoslynShapes" &&
                           reader.GetString(type.Name) == "Shapes")
            .SelectMany(type => type.GetMethods())
            .Single(handle => reader.GetString(reader.GetMethodDefinition(handle).Name) == methodName);
        var method = reader.GetMethodDefinition(methodHandle);
        var body = peReader.GetMethodBody(method.RelativeVirtualAddress);
        var bytes = body.GetILBytes()!;
        var observations = ImmutableArray.CreateBuilder<CilObservation>();
        var position = 0;
        while (position < bytes.Length)
        {
            var opCode = ReadOpCode(bytes, ref position);
            var operandSize = GetOperandSize(opCode.OperandType, bytes, position);
            if (opCode.OperandType is OperandType.InlineField or OperandType.InlineMethod or
                OperandType.InlineTok or OperandType.InlineType)
            {
                var token = BitConverter.ToInt32(bytes, position);
                var target = MetadataTokens.EntityHandle(token);
                var (owner, member) = ResolveTarget(reader, target);
                observations.Add(new CilObservation(
                    opCode.Name!,
                    target.Kind,
                    owner,
                    member,
                    ResolveMethodSignature(reader, target)));
            }
            position += operandSize;
        }
        return observations.ToImmutable();
    }

    private static OpCode ReadOpCode(byte[] bytes, ref int position)
    {
        var first = bytes[position++];
        var value = first == 0xfe
            ? (ushort)(0xfe00 | bytes[position++])
            : first;
        return OpCodesByValue[value];
    }

    private static int GetOperandSize(OperandType operandType, byte[] bytes, int position) =>
        operandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or
                OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or OperandType.InlineField or
                OperandType.InlineI or OperandType.InlineMethod or
                OperandType.InlineSig or OperandType.InlineString or
                OperandType.InlineTok or OperandType.InlineType or
                OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => checked(4 + BitConverter.ToInt32(bytes, position) * 4),
            _ => throw new InvalidOperationException($"Unsupported operand type {operandType}.")
        };

    private static (string Owner, string Member) ResolveTarget(
        MetadataReader reader,
        EntityHandle target) =>
        target.Kind switch
        {
            HandleKind.MemberReference => ResolveMemberReference(
                reader,
                reader.GetMemberReference((MemberReferenceHandle)target)),
            HandleKind.MethodSpecification => ResolveTarget(
                reader,
                reader.GetMethodSpecification((MethodSpecificationHandle)target).Method),
            HandleKind.MethodDefinition => ResolveMethodDefinition(
                reader,
                (MethodDefinitionHandle)target),
            HandleKind.FieldDefinition => ResolveFieldDefinition(
                reader,
                (FieldDefinitionHandle)target),
            HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification =>
                (ResolveTypeName(reader, target), string.Empty),
            _ => (target.Kind.ToString(), string.Empty)
        };

    private static string? ResolveMethodSignature(
        MetadataReader reader,
        EntityHandle target)
    {
        var provider = new SignatureTypeProvider(
            new NetWasm.Compiler.Core.AssemblyIdentity("ExpressionTreeRoslynShapes"),
            reader);
        return Resolve(target, CliGenericContext.Empty);

        string? Resolve(EntityHandle handle, CliGenericContext context)
        {
            MethodSignature<CliTypeIdentity> signature;
            switch (handle.Kind)
            {
                case HandleKind.MethodSpecification:
                    var specification = reader.GetMethodSpecification(
                        (MethodSpecificationHandle)handle);
                    var methodArguments = specification.DecodeSignature(
                        provider,
                        genericContext: null);
                    return Resolve(
                        specification.Method,
                        new CliGenericContext(context.TypeArguments, methodArguments));
                case HandleKind.MemberReference:
                    var reference = reader.GetMemberReference(
                        (MemberReferenceHandle)handle);
                    var typeArguments = reference.Parent.Kind ==
                            HandleKind.TypeSpecification
                        ? reader.GetTypeSpecification(
                                (TypeSpecificationHandle)reference.Parent)
                            .DecodeSignature(provider, genericContext: null)
                            .TypeArguments
                        : context.TypeArguments;
                    signature = reference.DecodeMethodSignature(
                        provider,
                        new CliGenericContext(typeArguments, context.MethodArguments));
                    break;
                case HandleKind.MethodDefinition:
                    signature = reader.GetMethodDefinition(
                            (MethodDefinitionHandle)handle)
                        .DecodeSignature(provider, context);
                    break;
                default:
                    return null;
            }
            return $"{(signature.Header.IsInstance ? "instance" : "static")}" +
                $"<{signature.GenericParameterCount.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)}> " +
                $"{signature.ReturnType.CanonicalName}(" +
                string.Join(",", signature.ParameterTypes.Select(
                    parameter => parameter.CanonicalName)) + ")";
        }
    }

    private static (string Owner, string Member) ResolveMemberReference(
        MetadataReader reader,
        MemberReference member) =>
        (ResolveTypeName(reader, member.Parent), reader.GetString(member.Name));

    private static (string Owner, string Member) ResolveMethodDefinition(
        MetadataReader reader,
        MethodDefinitionHandle handle)
    {
        var member = reader.GetMethodDefinition(handle);
        return (
            ResolveDeclaringTypeName(reader, handle),
            reader.GetString(member.Name));
    }

    private static (string Owner, string Member) ResolveFieldDefinition(
        MetadataReader reader,
        FieldDefinitionHandle handle)
    {
        var member = reader.GetFieldDefinition(handle);
        return (
            ResolveDeclaringTypeName(reader, handle),
            reader.GetString(member.Name));
    }

    private static string ResolveDeclaringTypeName(
        MetadataReader reader,
        EntityHandle memberHandle)
    {
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var type = reader.GetTypeDefinition(typeHandle);
            if (memberHandle.Kind == HandleKind.MethodDefinition &&
                type.GetMethods().Contains((MethodDefinitionHandle)memberHandle) ||
                memberHandle.Kind == HandleKind.FieldDefinition &&
                type.GetFields().Contains((FieldDefinitionHandle)memberHandle))
            {
                return reader.GetString(type.Name);
            }
        }
        throw new InvalidOperationException("Declaring type is unavailable.");
    }

    private static string ResolveTypeName(MetadataReader reader, EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.TypeDefinition => reader.GetString(
                reader.GetTypeDefinition((TypeDefinitionHandle)handle).Name),
            HandleKind.TypeReference => reader.GetString(
                reader.GetTypeReference((TypeReferenceHandle)handle).Name),
            HandleKind.TypeSpecification => "TypeSpecification",
            _ => handle.Kind.ToString()
        };

    private static byte[] Compile(bool optimized)
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))!
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var syntaxTree = CSharpSyntaxTree.ParseText(
            Source,
            new CSharpParseOptions(LanguageVersion.Latest));
        var compilation = CSharpCompilation.Create(
            "ExpressionTreeRoslynShapes",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: optimized ? OptimizationLevel.Release : OptimizationLevel.Debug,
                deterministic: true));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success);
        return stream.ToArray();
    }

    private static readonly string[] FactoryMethodNames =
    [
        "Arithmetic",
        "Conditional",
        "Property",
        "Field",
        "Constructor",
        "InstanceMethod",
        "StaticMethod",
        "Operator",
        "Conversion",
        "Closure",
        "Quote",
        "GenericCall",
        "GenericMethodCall",
    ];

    private sealed record CilObservation(
        string OpCode,
        HandleKind TargetKind,
        string Owner,
        string Member,
        string? Signature);

    private const string Source = """
        using System;
        using System.Linq.Expressions;

        namespace ExpressionTreeRoslynShapes;

        public sealed class Model
        {
            public int Field;

            public Model(int value)
            {
                Field = value;
            }

            public int Property => Field;

            public int Add(int value) => Field + value;

            public static Model operator +(Model value, int increment) =>
                new(value.Field + increment);

            public static explicit operator int(Model value) => value.Field;
        }

        public static class GenericBox<T>
        {
            public static T Identity(T value) => value;
        }

        public static class GenericMethods
        {
            public static T Identity<T>(T value) => value;
        }

        public static class Shapes
        {
            public static Expression<Func<int, int>> Arithmetic() =>
                value => value + 1;

            public static Expression<Func<int, int>> Conditional() =>
                value => value > 0 && value < 10 ? value : -value;

            public static Expression<Func<Model, int>> Property() =>
                model => model.Property;

            public static Expression<Func<Model, int>> Field() =>
                model => model.Field;

            public static Expression<Func<int, Model>> Constructor() =>
                value => new Model(value);

            public static Expression<Func<Model, int>> InstanceMethod() =>
                model => model.Add(1);

            public static Expression<Func<int, int>> StaticMethod() =>
                value => Twice(value);

            public static Expression<Func<Model, Model>> Operator() =>
                model => model + 1;

            public static Expression<Func<Model, int>> Conversion() =>
                model => (int)model;

            public static Expression<Func<int, int>> Closure(int captured) =>
                value => value + captured;

            public static Expression<Func<int, int>> Quote() =>
                value => Evaluate(inner => inner + 1, value);

            public static Expression<Func<int, int>> GenericCall() =>
                value => GenericBox<int>.Identity(value);

            public static Expression<Func<int, int>> GenericMethodCall() =>
                value => GenericMethods.Identity<int>(value);

            public static int RunAll(bool preferInterpretation)
            {
                if (Arithmetic().Compile(preferInterpretation)(41) != 42) return 1;
                if (Conditional().Compile(preferInterpretation)(5) != 5) return 2;
                if (Conditional().Compile(preferInterpretation)(-5) != 5) return 3;
                if (Property().Compile(preferInterpretation)(new Model(42)) != 42) return 4;
                if (Field().Compile(preferInterpretation)(new Model(42)) != 42) return 5;
                if (Constructor().Compile(preferInterpretation)(42).Field != 42) return 6;
                if (InstanceMethod().Compile(preferInterpretation)(new Model(41)) != 42) return 7;
                if (StaticMethod().Compile(preferInterpretation)(21) != 42) return 8;
                if (Operator().Compile(preferInterpretation)(new Model(41)).Field != 42) return 9;
                if (Conversion().Compile(preferInterpretation)(new Model(42)) != 42) return 10;
                if (Closure(1).Compile(preferInterpretation)(41) != 42) return 11;
                if (Quote().Compile(preferInterpretation)(41) != 42) return 12;
                if (GenericCall().Compile(preferInterpretation)(42) != 42) return 13;
                if (GenericMethodCall().Compile(preferInterpretation)(42) != 42) return 14;
                if (RunProgrammatic(preferInterpretation) != 42) return 15;
                return 42;
            }

            private static int RunProgrammatic(bool preferInterpretation)
            {
                var value = Expression.Parameter(typeof(int), "value");
                var local = Expression.Variable(typeof(int), "local");
                var inner = Expression.Lambda<Func<int, int>>(
                    Expression.Add(value, Expression.Constant(1)),
                    value);
                var block = Expression.Block(
                    [local],
                    Expression.Assign(local, Expression.Constant(41)),
                    Expression.Invoke(inner, local));
                return Expression.Lambda<Func<int>>(block).Compile(preferInterpretation)();
            }

            private static int Twice(int value) => value * 2;

            private static int Evaluate(Expression<Func<int, int>> expression, int value) =>
                expression.Compile(preferInterpretation: true)(value);
        }
        """;
}
