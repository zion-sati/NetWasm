using System.Collections.Immutable;
using NetWasm.Testing.CompilerHost;

namespace NetWasm.Testing.CompilerHost.Tests;

public sealed class CompilerHostRequestReaderTests
{
    [Fact]
    public void ReadsARequestUsingTheInjectedDeserializerOptions()
    {
        var path = Path.Combine(Path.GetTempPath(), $"compiler-host-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, """
                {
                  "ENTRYASSEMBLYPATH": "entry.dll",
                  "referencePaths": [],
                  "entryTypeName": "Application",
                  "entryMethodName": "Run",
                  "exports": [],
                  "target": 0,
                  "sourcePaths": [],
                  "referenceAssemblyAliases": {},
                  "modulePath": "module.wasm"
                }
                """);
            var reader = new CompilerHostRequestReader(
                new CompilerHostRequestDeserializer(
                    new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                    }));

            var request = reader.Read(path);

            Assert.Equal("entry.dll", request.EntryAssemblyPath);
            Assert.Equal("Application", request.EntryTypeName);
            Assert.Empty(request.ReferencePaths);
            Assert.Equal(ImmutableDictionary<string, string>.Empty, request.ReferenceAssemblyAliases);
            Assert.Null(request.RuntimeLayoutPath);
            Assert.Null(request.InteropManifestPath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void DeserializesOptionalLinkedArtifactPathsWithoutChangingAssemblyIdentity(int target)
    {
        var deserializer = new CompilerHostRequestDeserializer(
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        var json = $$"""
            {
              "entryAssemblyPath": "same-il.dll",
              "referencePaths": ["corelib.dll"],
              "entryTypeName": "Application",
              "entryMethodName": "Run",
              "exports": [],
              "target": {{target}},
              "sourcePaths": [],
              "referenceAssemblyAliases": {"System.Runtime": "NetWasm.CoreLib"},
              "modulePath": "module.wasm",
              "runtimeLayoutPath": "layout.json",
              "interopManifestPath": "interop.json"
            }
            """;

        var request = ((ICompilerHostRequestDeserializer)deserializer).Deserialize(json);

        Assert.Equal("same-il.dll", request.EntryAssemblyPath);
        Assert.Equal(target, (int)request.Target);
        Assert.Equal("layout.json", request.RuntimeLayoutPath);
        Assert.Equal("interop.json", request.InteropManifestPath);
        Assert.Equal("NetWasm.CoreLib", request.ReferenceAssemblyAliases["System.Runtime"]);
    }
}
