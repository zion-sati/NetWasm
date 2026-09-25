using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace NetWasm.Compiler.Tests.Correctness;

internal sealed class SyntaxTreeProfileCorpusTemplate : IGeneratedCorpusTemplate
{
    private static readonly ImmutableDictionary<string, Profile> Profiles =
        new Dictionary<string, Profile>(StringComparer.Ordinal)
        {
            ["arithmetic"] = new("arithmetic", "return unchecked(input * 3 + 7);"),
            ["control-flow"] = new("control-flow",
                "var value = input; for (var i = 0; i < 3; i++) value = (value & 1) == 0 ? value + i + 1 : value - i; return value;"),
            ["eh"] = new("eh",
                "var value = input; try { if (input < 0) throw new System.InvalidOperationException(); value += 5; } catch (System.InvalidOperationException) { value = -value; } finally { value += 3; } return value;"),
            ["iterator"] = new("iterator",
                "var total = 0; foreach (var value in Values(input)) { total += value; if (total > input + 4) break; } return total;",
                "private static System.Collections.Generic.IEnumerable<int> Values(int input) { try { yield return input; yield return input + 2; } finally { _trace += 11; } }"),
            ["async"] = new("async",
                "return Execute(input).Result;",
                "private static async System.Threading.Tasks.Task<int> Execute(int input) { try { await System.Threading.Tasks.Task.CompletedTask; return input + 5; } finally { _trace += 13; } }"),
            ["object-model"] = new("object-model",
                "IValue value = new Box(input); return value.Read() + 1;",
                "private interface IValue { int Read(); } private sealed class Box : IValue { private readonly int _value; public Box(int value) { _value = value; } public int Read() => _value; }"),
            ["mixed"] = new("mixed",
                "var total = 0; try { foreach (var value in Values(input)) total += value; return Finish(total).Result; } finally { _trace += 17; }",
                "private static System.Collections.Generic.IEnumerable<int> Values(int input) { yield return input; yield return input + 1; } private static async System.Threading.Tasks.Task<int> Finish(int value) { await System.Threading.Tasks.Task.CompletedTask; return value + 3; }"),
        }.ToImmutableDictionary(StringComparer.Ordinal);

    public string Family => "SyntaxTreeProfiles";
    public int Seed => 0x13572468;
    public ImmutableArray<CorpusDimension> Dimensions =>
    [
        new("profile", [.. Profiles.Keys.Order(StringComparer.Ordinal)]),
        new("construction", ["syntax-tree"]),
    ];
    public ImmutableArray<ImmutableDictionary<string, string>> TargetedCases => [];

    public CorpusFixture Create(
        string caseName,
        ImmutableDictionary<string, string> values)
    {
        var profile = Profiles[values["profile"]];
        var entry = SyntaxFactory.ClassDeclaration("EntryPoint")
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword))
            .AddMembers(
                SyntaxFactory.FieldDeclaration(
                        SyntaxFactory.VariableDeclaration(
                                SyntaxFactory.PredefinedType(
                                    SyntaxFactory.Token(SyntaxKind.IntKeyword)))
                            .AddVariables(SyntaxFactory.VariableDeclarator("_trace")
                                .WithInitializer(SyntaxFactory.EqualsValueClause(
                                    SyntaxFactory.LiteralExpression(
                                        SyntaxKind.NumericLiteralExpression,
                                        SyntaxFactory.Literal(0))))))
                    .AddModifiers(SyntaxFactory.Token(SyntaxKind.PrivateKeyword),
                        SyntaxFactory.Token(SyntaxKind.StaticKeyword)),
                Method("Run", profile.Body, parameter: true),
                Method("Trace", "return _trace;", parameter: false));
        if (profile.SupportingMembers is not null)
        {
            entry = entry.AddMembers(ParseMembers(profile).ToArray());
        }
        var root = SyntaxFactory.CompilationUnit()
            .AddMembers(SyntaxFactory.FileScopedNamespaceDeclaration(
                    SyntaxFactory.ParseName($"NetWasm.Correctness.Generated.{caseName}"))
                .AddMembers(entry))
            .NormalizeWhitespace();
        return new CorpusFixture(
            caseName,
            $"NetWasm.Correctness.Generated.{caseName}",
            root.ToFullString(),
            [-1, 0, 2])
        {
            RequiresReactor = profile.RequiresReactor,
        };
    }

    internal static ImmutableArray<string> FeatureManifest =>
        [.. Profiles.Values.Select(profile => profile.Name).Order(StringComparer.Ordinal)];

    private static MethodDeclarationSyntax Method(
        string name,
        string body,
        bool parameter)
    {
        var method = SyntaxFactory.MethodDeclaration(
                SyntaxFactory.PredefinedType(SyntaxFactory.Token(SyntaxKind.IntKeyword)),
                name)
            .AddModifiers(SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                SyntaxFactory.Token(SyntaxKind.StaticKeyword))
            .WithBody((BlockSyntax)SyntaxFactory.ParseStatement("{" + body + "}"));
        return parameter
            ? method.AddParameterListParameters(
                SyntaxFactory.Parameter(SyntaxFactory.Identifier("input"))
                    .WithType(SyntaxFactory.PredefinedType(
                        SyntaxFactory.Token(SyntaxKind.IntKeyword))))
            : method;
    }

    private static SyntaxList<MemberDeclarationSyntax> ParseMembers(Profile profile)
    {
        var wrapper = SyntaxFactory.ParseCompilationUnit(
            "class ProfileMembers {" + profile.SupportingMembers + "}");
        var members = wrapper.Members.OfType<ClassDeclarationSyntax>()
            .Single().Members;
        if (wrapper.ContainsDiagnostics || members.Count == 0)
        {
            throw new InvalidOperationException(
                $"profile '{profile.Name}' did not produce valid member trees");
        }
        return members;
    }

    private sealed record Profile(
        string Name,
        string Body,
        string? SupportingMembers = null)
    {
        public bool RequiresReactor => Name is "async" or "mixed";
    }
}
