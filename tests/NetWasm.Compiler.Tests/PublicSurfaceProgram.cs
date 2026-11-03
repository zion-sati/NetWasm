using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace NetWasm.Compiler.Tests;

// Writes programs that reach the public members of an assembly's public types:
// for each type, or each instantiation of a generic type with int and with
// string arguments, a call of each member with typed default arguments, each
// call in a method of its own that Run calls only for an input no test passes.
// So every member is compiled and none runs. A call C# rejects, such as an
// instantiation a constraint forbids, is left out.
internal static class PublicSurfaceProgram
{
    public const int UnusedInput = 0x5EED;

    private const int MaximumPruningRounds = 8;

    // The member calls of every chunk-th public type, starting at chunk.
    public static ImmutableArray<string> Calls(string assemblyPath, int chunk, int chunks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(chunk);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(chunk, chunks);
        var compilation = Compilation(assemblyPath, $"PublicSurface{chunk}");
        var assembly = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(compilation.References.Single())!;
        var arguments = new[]
        {
            compilation.GetSpecialType(SpecialType.System_Int32),
            compilation.GetSpecialType(SpecialType.System_String),
        };
        var calls = PublicTypes(assembly.GlobalNamespace)
            .OrderBy(type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal)
            .Where((_, index) => index % chunks == chunk)
            .SelectMany(type => Instantiate(type, arguments))
            .SelectMany(instance => Statements(instance, arguments))
            .ToList();
        for (var round = 0; ; round++)
        {
            var program = Program(compilation, calls);
            var lines = program.GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line)
                .ToHashSet();
            if (lines.Count == 0)
            {
                return [.. calls];
            }
            var rejected = lines.Select(CallAt).ToHashSet();
            if (rejected.Contains(-1) || round == MaximumPruningRounds)
            {
                throw new InvalidOperationException(
                    $"the public surface program does not compile: {program.GetDiagnostics().First(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error && CallAt(diagnostic.Location.GetLineSpan().StartLinePosition.Line) < 0)}");
            }
            calls = [.. calls.Where((_, index) => !rejected.Contains(index))];
        }
    }

    // The program that makes the calls, compiled.
    public static CSharpCompilation Compile(string assemblyPath, string name, IReadOnlyList<string> calls) =>
        Program(Compilation(assemblyPath, name), calls);

    // The name of the method that makes a call, as diagnostics name it.
    public static string MethodOf(int call) => $"S{call}";

    private static CSharpCompilation Compilation(string assemblyPath, string name) =>
        CSharpCompilation.Create(
            name,
            references: [MetadataReference.CreateFromFile(assemblyPath)],
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                optimizationLevel: OptimizationLevel.Release,
                generalDiagnosticOption: ReportDiagnostic.Suppress));

    // Call i is on line FirstCallLine + 2i, in method S{i} declared on the line above.
    private const int FirstCallLine = 4;

    private static int CallAt(int line) =>
        line >= FirstCallLine && (line - FirstCallLine) % 2 == 0 ? (line - FirstCallLine) / 2 : -1;

    private static CSharpCompilation Program(CSharpCompilation compilation, IReadOnlyList<string> calls)
    {
        var source = new StringBuilder()
            .Append("#pragma warning disable\n")
            .Append("public static unsafe class EntryPoint\n")
            .Append("{\n");
        for (var call = 0; call < calls.Count; call++)
        {
            source.Append("    private static void ").Append(MethodOf(call)).Append("()\n")
                .Append("        ").Append(calls[call]).Append('\n');
        }
        source.Append("    public static int Run(int input)\n    {\n")
            .Append("        if (input == ").Append(UnusedInput).Append(")\n        {\n");
        for (var call = 0; call < calls.Count; call++)
        {
            source.Append("            ").Append(MethodOf(call)).Append("();\n");
        }
        source.Append("        }\n        return input;\n    }\n}\n");
        return compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(CSharpSyntaxTree.ParseText(source.ToString()));
    }

    private static IEnumerable<INamedTypeSymbol> PublicTypes(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            foreach (var nested in Nested(type))
            {
                yield return nested;
            }
        }
        foreach (var child in @namespace.GetNamespaceMembers())
        {
            foreach (var type in PublicTypes(child))
            {
                yield return type;
            }
        }

        static IEnumerable<INamedTypeSymbol> Nested(INamedTypeSymbol type)
        {
            if (type.DeclaredAccessibility != Accessibility.Public)
            {
                yield break;
            }
            yield return type;
            foreach (var nested in type.GetTypeMembers().SelectMany(Nested))
            {
                yield return nested;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> Instantiate(INamedTypeSymbol type, ITypeSymbol[] arguments)
    {
        if (!IsGeneric(type))
        {
            return [type];
        }
        return arguments.Select(argument => ConstructWithin(type, argument));

        static bool IsGeneric(INamedTypeSymbol type) =>
            type.Arity > 0 || type.ContainingType is { } outer && IsGeneric(outer);

        static INamedTypeSymbol ConstructWithin(INamedTypeSymbol type, ITypeSymbol argument)
        {
            var container = type.ContainingType is { } outer ? ConstructWithin(outer, argument) : null;
            var definition = container is null
                ? type
                : container.GetTypeMembers(type.Name, type.Arity).Single(member =>
                    SymbolEqualityComparer.Default.Equals(member.OriginalDefinition, type.OriginalDefinition));
            return definition.Arity == 0
                ? definition
                : definition.Construct([.. Enumerable.Repeat(argument, definition.Arity)]);
        }
    }

    private static IEnumerable<string> Statements(INamedTypeSymbol type, ITypeSymbol[] arguments)
    {
        if (type.TypeKind is TypeKind.Delegate or TypeKind.Enum)
        {
            yield break;
        }
        var name = Display(type);
        var receiver = type.IsValueType ? $"default({name})" : $"(({name})null)";
        foreach (var member in type.GetMembers())
        {
            if (member.DeclaredAccessibility != Accessibility.Public || member.IsImplicitlyDeclared ||
                member.IsStatic && member.IsAbstract)
            {
                continue;
            }
            var target = member.IsStatic ? name : receiver;
            switch (member)
            {
                case IMethodSymbol { MethodKind: MethodKind.Ordinary, IsVararg: false } method:
                    foreach (var instance in method.IsGenericMethod
                                 ? arguments.Select(argument =>
                                     method.Construct([.. Enumerable.Repeat(argument, method.Arity)]))
                                 : [method])
                    {
                        var typeArguments = instance.IsGenericMethod
                            ? $"<{string.Join(", ", instance.TypeArguments.Select(Display))}>"
                            : string.Empty;
                        yield return Call(instance.Parameters, call => $"{target}.{Escape(method.Name)}{typeArguments}({call});");
                    }
                    break;
                case IMethodSymbol { MethodKind: MethodKind.Constructor } constructor
                    when !type.IsAbstract && !type.IsStatic && type.TypeKind != TypeKind.Interface:
                    yield return Call(constructor.Parameters, call => $"_ = new {name}({call});");
                    break;
                case IPropertySymbol property:
                    var access = property.IsIndexer
                        ? (Func<string, string>)(call => $"{target}[{call}]")
                        : _ => $"{target}.{Escape(property.Name)}";
                    if (property.GetMethod is { DeclaredAccessibility: Accessibility.Public })
                    {
                        yield return Call(property.Parameters, call => $"_ = {access(call)};");
                    }
                    if (property.SetMethod is { DeclaredAccessibility: Accessibility.Public, IsInitOnly: false })
                    {
                        yield return Call(property.Parameters,
                            call => $"{access(call)} = default({Display(property.Type)});");
                    }
                    break;
                case IFieldSymbol field:
                    yield return $"{{ _ = {target}.{Escape(field.Name)}; }}";
                    break;
            }
        }
    }

    // Each argument is a typed default; one passed by reference reads a local.
    private static string Call(ImmutableArray<IParameterSymbol> parameters, Func<string, string> expression)
    {
        var locals = new StringBuilder();
        var arguments = new List<string>();
        foreach (var parameter in parameters)
        {
            var type = Display(parameter.Type);
            if (parameter.RefKind == RefKind.None)
            {
                arguments.Add($"default({type})");
                continue;
            }
            var local = $"a{arguments.Count}";
            locals.Append(type).Append(' ').Append(local).Append(" = default; ");
            arguments.Add(parameter.RefKind switch
            {
                RefKind.Out => $"out {local}",
                RefKind.In or RefKind.RefReadOnlyParameter => $"in {local}",
                _ => $"ref {local}",
            });
        }
        return $"{{ {locals}{expression(string.Join(", ", arguments))} }}";
    }

    private static string Display(ITypeSymbol type) => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static string Escape(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None ? name : "@" + name;
}
