using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NetWasm.Sdk.Pack.Tests;

public sealed partial class SdkToolchainContractTests
{
    // MSBuild evaluates a target's condition before it runs the targets the target depends
    // on, so a condition that reads what those targets produce sees it as it was before
    // them: _NetWasmPublishSiteFiles read the site files its own dependencies declare,
    // and published no site without a build that had collected them first.
    [Fact]
    public void NoTargetConditionReadsWhatItsDependenciesProduce()
    {
        var documents = Directory.EnumerateFiles(Path.Combine(FindRepositoryRoot(), "src"), "*", SearchOption.AllDirectories)
            .Where(static path => path.EndsWith(".targets", StringComparison.Ordinal) ||
                path.EndsWith(".props", StringComparison.Ordinal))
            .Where(static path => !path.Split(Path.DirectorySeparatorChar).Any(static part => part is "bin" or "obj"))
            .Select(static path => XDocument.Load(path).Root!)
            .ToArray();
        var properties = documents
            .SelectMany(static root => root.Elements().Where(static element => element.Name.LocalName == "PropertyGroup"))
            .SelectMany(static group => group.Elements())
            .ToLookup(static property => property.Name.LocalName, static property => property.Value);
        var targets = documents
            .SelectMany(static root => root.Elements().Where(static element => element.Name.LocalName == "Target"))
            .ToLookup(static target => (string)target.Attribute("Name")!);

        var violations = new List<string>();
        foreach (var target in targets.SelectMany(static group => group))
        {
            var condition = (string?)target.Attribute("Condition");
            var dependsOn = (string?)target.Attribute("DependsOnTargets");
            if (condition is null || dependsOn is null)
            {
                continue;
            }

            var dependencies = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>(TargetNames(dependsOn, properties));
            while (pending.TryPop(out var name))
            {
                if (dependencies.Add(name))
                {
                    foreach (var dependency in targets[name])
                    {
                        foreach (var next in TargetNames((string?)dependency.Attribute("DependsOnTargets") ?? "", properties))
                        {
                            pending.Push(next);
                        }
                    }
                }
            }

            var produced = dependencies.SelectMany(name => targets[name]).SelectMany(Produces).ToHashSet(StringComparer.Ordinal);
            var read = Regex.Matches(condition, @"[@$]\((\w+)").Select(static match => match.Groups[1].Value);
            violations.AddRange(read.Where(produced.Contains).Distinct()
                .Select(name => $"{target.Attribute("Name")!.Value} reads {name}"));
        }

        Assert.Empty(violations);
    }

    // The targets a DependsOnTargets list names, with the lists the properties it names hold.
    private static IEnumerable<string> TargetNames(string list, ILookup<string, string> properties, int depth = 0)
    {
        foreach (var entry in list.Split([';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var property = Regex.Match(entry, @"^\$\((\w+)\)$");
            if (!property.Success)
            {
                yield return entry;
            }
            else if (depth < 4)
            {
                foreach (var name in properties[property.Groups[1].Value].SelectMany(value => TargetNames(value, properties, depth + 1)))
                {
                    yield return name;
                }
            }
        }
    }

    // The items and properties a target declares, directly or as a task's output.
    private static IEnumerable<string> Produces(XElement target) =>
        target.Descendants()
            .Where(static element => element.Name.LocalName is "ItemGroup" or "PropertyGroup")
            .SelectMany(static group => group.Elements())
            .Select(static declared => declared.Name.LocalName)
            .Concat(target.Descendants()
                .Where(static element => element.Name.LocalName == "Output")
                .Select(static output => (string?)output.Attribute("ItemName") ?? (string?)output.Attribute("PropertyName"))
                .OfType<string>());
}
